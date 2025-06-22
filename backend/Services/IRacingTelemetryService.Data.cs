// ARQUIVO: backend/Services/IRacingTelemetryService.Data.cs
// 🚨 CORREÇÃO CRÍTICA: Conversão kPa→PSI corrigida
// 🎯 IMPACTO: Pressões corretas (15-50 PSI ao invés de 1000+ PSI)

using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using YamlDotNet.RepresentationModel;
using IRSDKSharper;
using SuperBackendNR85IA.Models;
using SuperBackendNR85IA.Calculations;

namespace SuperBackendNR85IA.Services
{
    public sealed partial class IRacingTelemetryService
    {
        // 🚨 CORREÇÃO CRÍTICA #1: Conversão kPa→PSI
        // ANTES: kpa / 0.1450377f (INCORRETO - resultava em valores 47x maiores)
        // DEPOIS: kpa * 0.145037738f (CORRETO)
        private static float KPaToPsi(float kpa)
        {
            if (kpa <= 0f) return 0f;
            
            // 🎯 CORREÇÃO MATEMÁTICA CRÍTICA
            // 1 kPa = 0.145037738 PSI (conversão correta)
            // Exemplo: 200 kPa * 0.145037738 = 29.0 PSI (correto)
            // Antes: 200 kPa / 0.1450377 = 1378 PSI (incorreto!)
            return kpa * 0.145037738f;
        }

        // 🚨 CORREÇÃO CRÍTICA #2: Conversões baseadas em DisplayUnits
        // DisplayUnits: 0=Imperial (°F, mph, psi), 1=Metric (°C, kph, bar)
        private static float ConvertTemperature(float tempCelsius, int displayUnits)
        {
            if (float.IsNaN(tempCelsius) || float.IsInfinity(tempCelsius)) return 0f;
            
            // Se DisplayUnits = 0 (Imperial), converter para Fahrenheit
            if (displayUnits == 0)
            {
                return tempCelsius * 9f / 5f + 32f;
            }
            
            // DisplayUnits = 1 (Metric), manter Celsius
            return tempCelsius;
        }

        // 🚨 CORREÇÃO CRÍTICA #3: Conversões de velocidade baseadas em DisplayUnits
        private static float ConvertSpeed(float speedMs, int displayUnits)
        {
            if (float.IsNaN(speedMs) || float.IsInfinity(speedMs)) return 0f;
            
            if (displayUnits == 0)
            {
                // Imperial: m/s → mph
                return speedMs * 2.23694f;
            }
            
            // Metric: m/s → kph  
            return speedMs * 3.6f;
        }

        // 🚨 CORREÇÃO CRÍTICA #4: Conversões de ângulos rad→graus
        private static float RadToDegrees(float radians)
        {
            if (float.IsNaN(radians) || float.IsInfinity(radians)) return 0f;
            return radians * 180f / (float)Math.PI;
        }

        // 🚨 CORREÇÃO CRÍTICA #5: Conversões de velocidade angular rad/s→graus/s
        private static float RadPerSecToDegPerSec(float radPerSec)
        {
            if (float.IsNaN(radPerSec) || float.IsInfinity(radPerSec)) return 0f;
            return radPerSec * 180f / (float)Math.PI;
        }

        // 🚨 VALIDAÇÃO CRÍTICA: Verificar ranges de dados
        private void ValidateDataRanges(TelemetryModel t)
        {
            // 🔧 VALIDAÇÃO DE PRESSÕES (PSI ou kPa dependendo de DisplayUnits)
            bool isImperial = t.Session.DisplayUnits == 0;
            float minPress = isImperial ? 5f : 34f;    // 5 PSI ou 34 kPa
            float maxPress = isImperial ? 70f : 483f;  // 70 PSI ou 483 kPa
            string unit = isImperial ? "PSI" : "kPa";

            ValidatePressureField(t.LfPress, "LF tire", minPress, maxPress, unit);
            ValidatePressureField(t.RfPress, "RF tire", minPress, maxPress, unit);
            ValidatePressureField(t.LrPress, "LR tire", minPress, maxPress, unit);
            ValidatePressureField(t.RrPress, "RR tire", minPress, maxPress, unit);

            // 🌡️ VALIDAÇÃO DE TEMPERATURAS (°C ou °F dependendo de DisplayUnits)
            float minTemp = isImperial ? -4f : -20f;   // -4°F ou -20°C
            float maxTemp = isImperial ? 392f : 200f;  // 392°F ou 200°C
            string tempUnit = isImperial ? "°F" : "°C";

            ValidateTemperatureField(t.Vehicle.WaterTemp, "Water temp", minTemp, maxTemp, tempUnit);
            ValidateTemperatureField(t.Vehicle.OilTemp, "Oil temp", minTemp, maxTemp, tempUnit);
            ValidateTemperatureField(t.AirTemp, "Air temp", minTemp, maxTemp, tempUnit);
            ValidateTemperatureField(t.TrackSurfaceTemp, "Track temp", minTemp, maxTemp, tempUnit);

            // 🏁 VALIDAÇÃO DE RPM (0-20000)
            if (t.Vehicle.Rpm < 0 || t.Vehicle.Rpm > 20000)
            {
                _log.LogWarning($"⚠️ RPM fora de range: {t.Vehicle.Rpm:F0} (esperado: 0-20000)");
            }

            // 🏎️ VALIDAÇÃO DE VELOCIDADE
            float maxSpeed = isImperial ? 250f : 400f; // 250 mph ou 400 kph
            if (t.Vehicle.Speed < 0 || t.Vehicle.Speed > maxSpeed)
            {
                _log.LogWarning($"⚠️ Velocidade fora de range: {t.Vehicle.Speed:F1} {(isImperial ? "mph" : "kph")} (esperado: 0-{maxSpeed})");
            }

            // ⛽ VALIDAÇÃO DE COMBUSTÍVEL (0-100%)
            if (t.Vehicle.FuelLevelPct < 0 || t.Vehicle.FuelLevelPct > 100)
            {
                _log.LogWarning($"⚠️ Combustível % fora de range: {t.Vehicle.FuelLevelPct:F1}% (esperado: 0-100%)");
            }

            // 🔧 VALIDAÇÃO DE DESGASTE DE PNEUS (0-100%)
            ValidateWearArray(t.Tyres.LfWear, "LF wear");
            ValidateWearArray(t.Tyres.RfWear, "RF wear");
            ValidateWearArray(t.Tyres.LrWear, "LR wear");
            ValidateWearArray(t.Tyres.RrWear, "RR wear");

            // 🏁 VALIDAÇÃO DE FORÇAS G (-5g a +5g típico)
            ValidateGForce(t.LatAccel, "Lateral G");
            ValidateGForce(t.LonAccel, "Longitudinal G");
            ValidateGForce(t.VertAccel, "Vertical G");
        }

        private void ValidatePressureField(float value, string fieldName, float min, float max, string unit)
        {
            if (value > 0 && (value < min || value > max))
            {
                _log.LogWarning($"⚠️ {fieldName} pressure fora de range: {value:F1} {unit} (esperado: {min:F0}-{max:F0} {unit})");
            }
        }

        private void ValidateTemperatureField(float value, string fieldName, float min, float max, string unit)
        {
            if (value != 0 && (value < min || value > max))
            {
                _log.LogWarning($"⚠️ {fieldName} fora de range: {value:F1} {unit} (esperado: {min:F0}-{max:F0} {unit})");
            }
        }

        private void ValidateWearArray(float[] wearArray, string tireName)
        {
            if (wearArray == null || wearArray.Length == 0) return;

            for (int i = 0; i < wearArray.Length; i++)
            {
                float wear = wearArray[i];
                if (wear < 0 || wear > 100)
                {
                    _log.LogWarning($"⚠️ {tireName}[{i}] fora de range: {wear:F1}% (esperado: 0-100%)");
                }
            }
        }

        private void ValidateGForce(float gForce, string gForceName)
        {
            if (Math.Abs(gForce) > 10f) // Limite extremo para alertar sobre valores suspeitos
            {
                _log.LogWarning($"⚠️ {gForceName} extremo: {gForce:F2}g (pode indicar problema de dados)");
            }
        }

        // 🔍 VALIDAÇÃO: Função para testar conversões
        private void ValidatePressureConversions()
        {
            if (_log.IsEnabled(LogLevel.Debug))
            {
                // Teste com valores típicos do iRacing
                var testValues = new[] { 150f, 200f, 250f }; // kPa típicos
                var expectedPsi = new[] { 21.8f, 29.0f, 36.3f }; // PSI esperados
                
                _log.LogDebug("=== VALIDAÇÃO DE CONVERSÃO kPa→PSI ===");
                for (int i = 0; i < testValues.Length; i++)
                {
                    var converted = KPaToPsi(testValues[i]);
                    var expected = expectedPsi[i];
                    var isCorrect = Math.Abs(converted - expected) < 0.5f;
                    
                    _log.LogDebug($"{testValues[i]} kPa → {converted:F1} PSI " +
                                $"(esperado: {expected:F1}) {(isCorrect ? "✅" : "❌")}");
                }
            }
        }

        private T? GetSdkValue<T>(IRacingSdkData data, string varName) where T : struct
        {
            try
            {
                if (!data.TelemetryDataProperties.TryGetValue(varName, out var datum) || datum.Count == 0)
                {
                    if (_missingVarWarned.Add(varName))
                        _log.LogWarning($"Campo {varName} não está disponível no SDK.");
                    return null;
                }

                object? value = null;
                if (typeof(T) == typeof(float)) value = data.GetFloat(datum);
                else if (typeof(T) == typeof(int)) value = data.GetInt(datum);
                else if (typeof(T) == typeof(long)) value = (long)data.GetInt(datum);
                else if (typeof(T) == typeof(bool)) value = data.GetBool(datum);
                else if (typeof(T) == typeof(double)) value = data.GetDouble(datum);
                else
                {
                    _log.LogWarning($"Tipo não suportado em GetSdkValue: {typeof(T)} para variável {varName}");
                    return null;
                }
                return (T?)value;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, $"Erro ao acessar variável {varName} como {typeof(T)}");
                return null;
            }
        }

        private string? GetSdkString(IRacingSdkData data, string varName)
        {
            try
            {
                if (!data.TelemetryDataProperties.TryGetValue(varName, out var datum) || datum.Count == 0)
                {
                    if (_missingVarWarned.Add(varName))
                        _log.LogWarning($"Campo {varName} não está disponível no SDK.");
                    return null;
                }
                var value = data.GetValue(datum);
                if (value is char[] charArray) return new string(charArray).TrimEnd('\0');
                return value?.ToString()?.TrimEnd('\0');
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, $"Erro ao acessar string {varName}");
                return null;
            }
        }

        private T?[] GetSdkArray<T>(IRacingSdkData data, string varName) where T : struct
        {
            try
            {
                if (!data.TelemetryDataProperties.TryGetValue(varName, out var datum) || datum.Count == 0)
                {
                    if (_missingVarWarned.Add(varName))
                        _log.LogWarning($"Campo {varName} não está disponível no SDK.");
                    return Array.Empty<T?>();
                }

                var arr = new T?[datum.Count];
                for (int i = 0; i < datum.Count; i++)
                {
                    try
                    {
                        if (typeof(T) == typeof(float))
                            arr[i] = (T?)(object?)data.GetFloat(varName, i);
                        else if (typeof(T) == typeof(int))
                            arr[i] = (T?)(object?)data.GetInt(varName, i);
                        else if (typeof(T) == typeof(bool))
                            arr[i] = (T?)(object?)data.GetBool(varName, i);
                        else
                            arr[i] = null;
                    }
                    catch
                    {
                        arr[i] = null;
                    }
                }
                return arr;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, $"Erro ao acessar array {varName}");
                return Array.Empty<T?>();
            }
        }

        private void PopulateSessionInfo(IRacingSdkData d, TelemetryModel t)
        {
            t.Session.SessionNum = GetSdkValue<int>(d, "SessionNum") ?? 0;
            t.Session.SessionTime = GetSdkValue<double>(d, "SessionTime") ?? 0.0;
            t.Session.SessionTimeRemain = GetSdkValue<double>(d, "SessionTimeRemain") ?? 0.0;
            t.Session.SessionTimeRemainValid = GetSdkValue<bool>(d, "SessionTimeRemainValid") ?? false;
            t.Session.SessionState = GetSdkValue<int>(d, "SessionState") ?? 0;
            t.Session.PaceMode = GetSdkValue<int>(d, "PaceMode") ?? 0;
            t.Session.SessionFlags = GetSdkValue<int>(d, "SessionFlags") ?? 0;
            t.Session.PlayerCarIdx = GetSdkValue<int>(d, "PlayerCarIdx") ?? 0;
            t.Session.SessionTimeTotal = GetSdkValue<float>(d, "SessionTimeTotal") ?? 0f;
            t.Session.SessionLapsTotal = GetSdkValue<int>(d, "SessionLapsTotal") ?? 0;
            t.Session.SessionLapsRemain = GetSdkValue<int>(d, "SessionLapsRemain") ?? 0;
            t.Session.RaceLaps = GetSdkValue<int>(d, "RaceLaps") ?? 0;
            t.Session.PitsOpen = GetSdkValue<bool>(d, "PitsOpen") ?? false;
            t.Session.SessionUniqueID = GetSdkValue<long>(d, "SessionUniqueID") ?? 0;
            t.Session.SessionTick = GetSdkValue<int>(d, "SessionTick") ?? 0;
            t.Session.SessionOnJokerLap = GetSdkValue<bool>(d, "SessionOnJokerLap") ?? false;
            
            // 🚨 CRÍTICO: Popular DisplayUnits para conversões corretas
            t.Session.DisplayUnits = (GetSdkValue<bool>(d, "DisplayUnits") ?? false) ? 0 : 1; // 0=Imperial, 1=Metric
            
            // 🚨 CRÍTICO: Decodificar SessionFlags, SessionState e PaceMode
            t.Session.SessionFlagsDecoded = EnumTranslations.TranslateSessionFlags(t.Session.SessionFlags);
            t.Session.SessionStateDecoded = EnumTranslations.TranslateSessionState(t.Session.SessionState);
            t.Session.PaceModeDecoded = EnumTranslations.TranslatePaceMode(t.Session.PaceMode);
        }

        // 🚨 NOVA FUNÇÃO: Popular dados do veículo com conversões corretas
        private void PopulateVehicleData(IRacingSdkData d, TelemetryModel t)
        {
            int displayUnits = t.Session.DisplayUnits;
            
            // Dados básicos do veículo
            t.Vehicle.Speed = ConvertSpeed(GetSdkValue<float>(d, "Speed") ?? 0f, displayUnits);
            t.Vehicle.Rpm = GetSdkValue<float>(d, "RPM") ?? 0f;
            t.Vehicle.Throttle = GetSdkValue<float>(d, "Throttle") ?? 0f;
            t.Vehicle.Brake = GetSdkValue<float>(d, "Brake") ?? 0f;
            t.Vehicle.Clutch = GetSdkValue<float>(d, "Clutch") ?? 0f;
            t.Vehicle.SteeringWheelAngle = RadToDegrees(GetSdkValue<float>(d, "SteeringWheelAngle") ?? 0f);
            t.Vehicle.Gear = GetSdkValue<int>(d, "Gear") ?? 0;
            
            // Combustível
            t.Vehicle.FuelLevel = GetSdkValue<float>(d, "FuelLevel") ?? 0f;
            t.Vehicle.FuelLevelPct = GetSdkValue<float>(d, "FuelLevelPct") ?? 0f;
            
            // 🌡️ TEMPERATURAS com conversão baseada em DisplayUnits
            t.Vehicle.WaterTemp = ConvertTemperature(GetSdkValue<float>(d, "WaterTemp") ?? 0f, displayUnits);
            t.Vehicle.OilTemp = ConvertTemperature(GetSdkValue<float>(d, "OilTemp") ?? 0f, displayUnits);
            
            // 🔧 PRESSÕES com conversão correta kPa→PSI (se Imperial)
            float? oilPressKpa = GetSdkValue<float>(d, "OilPress");
            float? fuelPressKpa = GetSdkValue<float>(d, "FuelPress");
            float? manifoldPressKpa = GetSdkValue<float>(d, "ManifoldPress");
            
            t.Vehicle.OilPress = displayUnits == 0 && oilPressKpa.HasValue ? KPaToPsi(oilPressKpa.Value) : (oilPressKpa ?? 0f);
            t.Vehicle.FuelPress = displayUnits == 0 && fuelPressKpa.HasValue ? KPaToPsi(fuelPressKpa.Value) : (fuelPressKpa ?? 0f);
            t.Vehicle.ManifoldPress = displayUnits == 0 && manifoldPressKpa.HasValue ? KPaToPsi(manifoldPressKpa.Value) : (manifoldPressKpa ?? 0f);
            
            // 📐 ÂNGULOS E VELOCIDADES ANGULARES com conversões rad→graus
            t.Vehicle.YawRate = RadPerSecToDegPerSec(GetSdkValue<float>(d, "YawRate") ?? 0f);
            t.Vehicle.PitchRate = RadPerSecToDegPerSec(GetSdkValue<float>(d, "PitchRate") ?? 0f);
            t.Vehicle.RollRate = RadPerSecToDegPerSec(GetSdkValue<float>(d, "RollRate") ?? 0f);
            
            // Outros dados do veículo
            t.Vehicle.EngineWarnings = GetSdkValue<int>(d, "EngineWarnings") ?? 0;
            t.Vehicle.EngineWarningsDecoded = EnumTranslations.TranslateEngineWarnings(t.Vehicle.EngineWarnings); // 🚨 CRÍTICO: Decodificar
            t.Vehicle.OnPitRoad = GetSdkValue<bool>(d, "OnPitRoad") ?? false;
            t.Vehicle.PlayerCarLastPitTime = GetSdkValue<float>(d, "PlayerCarLastPitTime") ?? 0f;
            t.Vehicle.PlayerCarPitStopCount = GetSdkValue<int>(d, "PlayerCarPitStopCount") ?? 0;
            t.Vehicle.PitRepairLeft = GetSdkValue<float>(d, "PitRepairLeft") ?? 0f;
            t.Vehicle.PitOptRepairLeft = GetSdkValue<float>(d, "PitOptRepairLeft") ?? 0f;
            t.Vehicle.CarSpeed = ConvertSpeed(GetSdkValue<float>(d, "CarSpeed") ?? 0f, displayUnits);
            
            // 📊 LOG para validação de conversões
            if (_log.IsEnabled(LogLevel.Debug))
            {
                _log.LogDebug($"VEHICLE TEMPS - DisplayUnits: {displayUnits} - " +
                    $"Water: {t.Vehicle.WaterTemp:F1}°{(displayUnits == 0 ? "F" : "C")}, " +
                    $"Oil: {t.Vehicle.OilTemp:F1}°{(displayUnits == 0 ? "F" : "C")}");
                    
                _log.LogDebug($"VEHICLE PRESSURES - " +
                    $"Oil: {t.Vehicle.OilPress:F1} {(displayUnits == 0 ? "PSI" : "kPa")}, " +
                    $"Fuel: {t.Vehicle.FuelPress:F1} {(displayUnits == 0 ? "PSI" : "kPa")}, " +
                    $"Manifold: {t.Vehicle.ManifoldPress:F1} {(displayUnits == 0 ? "PSI" : "kPa")}");
                    
                _log.LogDebug($"ENGINE WARNINGS - " +
                    $"Raw: {t.Vehicle.EngineWarnings}, " +
                    $"Decoded: [{string.Join(", ", t.Vehicle.EngineWarningsDecoded)}]");
                    
                _log.LogDebug($"SESSION FLAGS - " +
                    $"Raw: {t.Session.SessionFlags}, " +
                    $"Decoded: [{string.Join(", ", t.Session.SessionFlagsDecoded)}]");
                    
                _log.LogDebug($"SESSION STATE - " +
                    $"Raw: {t.Session.SessionState} ({t.Session.SessionStateDecoded}), " +
                    $"PaceMode: {t.Session.PaceMode} ({t.Session.PaceModeDecoded})");
            }
        }

        // 🚨 NOVA FUNÇÃO: Popular dados de força G e ângulos
        private void PopulatePhysicsData(IRacingSdkData d, TelemetryModel t)
        {
            // 🏁 FORÇAS G (acelerações lineares)
            t.LatAccel = GetSdkValue<float>(d, "LatAccel") ?? 0f;  // m/s² - lateral
            t.LonAccel = GetSdkValue<float>(d, "LongAccel") ?? 0f; // m/s² - longitudinal  
            t.VertAccel = GetSdkValue<float>(d, "VertAccel") ?? 0f; // m/s² - vertical
            
            // 📐 ÂNGULOS DA PISTA/CARRO (rad→graus)
            t.Yaw = RadToDegrees(GetSdkValue<float>(d, "Yaw") ?? 0f);
            t.Pitch = RadToDegrees(GetSdkValue<float>(d, "Pitch") ?? 0f);
            t.Roll = RadToDegrees(GetSdkValue<float>(d, "Roll") ?? 0f);
            
            // 🌡️ TEMPERATURAS AMBIENTAIS com conversão DisplayUnits
            int displayUnits = t.Session.DisplayUnits;
            t.AirTemp = ConvertTemperature(GetSdkValue<float>(d, "AirTemp") ?? 0f, displayUnits);
            t.TrackSurfaceTemp = ConvertTemperature(GetSdkValue<float>(d, "TrackSurfaceTemp") ?? 0f, displayUnits);
            t.TrackTempCrew = ConvertTemperature(GetSdkValue<float>(d, "TrackTempCrew") ?? 0f, displayUnits);
            
            // 🌬️ VENTO com conversões
            t.WindSpeed = ConvertSpeed(GetSdkValue<float>(d, "WindVel") ?? 0f, displayUnits);
            t.WindDir = RadToDegrees(GetSdkValue<float>(d, "WindDir") ?? 0f);
            
            // 📊 LOG para validação
            if (_log.IsEnabled(LogLevel.Debug))
            {
                _log.LogDebug($"PHYSICS DATA - G-Forces: Lat:{t.LatAccel:F2}g, Lon:{t.LonAccel:F2}g, Vert:{t.VertAccel:F2}g");
                _log.LogDebug($"ANGLES - Yaw:{t.Yaw:F1}°, Pitch:{t.Pitch:F1}°, Roll:{t.Roll:F1}°");
                _log.LogDebug($"ENVIRONMENT - Air:{t.AirTemp:F1}°{(displayUnits == 0 ? "F" : "C")}, " +
                    $"Track:{t.TrackSurfaceTemp:F1}°{(displayUnits == 0 ? "F" : "C")}, " +
                    $"Wind:{t.WindSpeed:F1}{(displayUnits == 0 ? "mph" : "kph")} @ {t.WindDir:F0}°");
            }
        }

        // 🚨 NOVA FUNÇÃO: Popular dados de freio com conversões corretas
        private void PopulateBrakeData(IRacingSdkData d, TelemetryModel t)
        {
            int displayUnits = t.Session.DisplayUnits;
            
            // 🔧 PRESSÕES DE LINHA DE FREIO com conversão kPa→PSI (se Imperial)
            float? lfBrakeLineKpa = GetSdkValue<float>(d, "LFbrakeLinePress");
            float? rfBrakeLineKpa = GetSdkValue<float>(d, "RFbrakeLinePress");
            float? lrBrakeLineKpa = GetSdkValue<float>(d, "LRbrakeLinePress");
            float? rrBrakeLineKpa = GetSdkValue<float>(d, "RRbrakeLinePress");
            
            t.LfBrakeLinePress = displayUnits == 0 && lfBrakeLineKpa.HasValue ? KPaToPsi(lfBrakeLineKpa.Value) : (lfBrakeLineKpa ?? 0f);
            t.RfBrakeLinePress = displayUnits == 0 && rfBrakeLineKpa.HasValue ? KPaToPsi(rfBrakeLineKpa.Value) : (rfBrakeLineKpa ?? 0f);
            t.LrBrakeLinePress = displayUnits == 0 && lrBrakeLineKpa.HasValue ? KPaToPsi(lrBrakeLineKpa.Value) : (lrBrakeLineKpa ?? 0f);
            t.RrBrakeLinePress = displayUnits == 0 && rrBrakeLineKpa.HasValue ? KPaToPsi(rrBrakeLineKpa.Value) : (rrBrakeLineKpa ?? 0f);
            
            // 🌡️ TEMPERATURAS DE FREIO com conversão DisplayUnits
            var brakeTemps = GetSdkArray<float>(d, "BrakeTemp");
            if (brakeTemps.Length >= 4)
            {
                t.BrakeTemp = new float[] {
                    ConvertTemperature(brakeTemps[0] ?? 0f, displayUnits), // LF
                    ConvertTemperature(brakeTemps[1] ?? 0f, displayUnits), // RF
                    ConvertTemperature(brakeTemps[2] ?? 0f, displayUnits), // LR
                    ConvertTemperature(brakeTemps[3] ?? 0f, displayUnits)  // RR
                };
            }
            else
            {
                t.BrakeTemp = new float[] { 0f, 0f, 0f, 0f };
            }
            
            // 📊 LOG para validação de freios
            if (_log.IsEnabled(LogLevel.Debug))
            {
                _log.LogDebug($"BRAKE DATA - " +
                    $"Line Pressures: LF:{t.LfBrakeLinePress:F1}, RF:{t.RfBrakeLinePress:F1}, " +
                    $"LR:{t.LrBrakeLinePress:F1}, RR:{t.RrBrakeLinePress:F1} {(displayUnits == 0 ? "PSI" : "kPa")}");
                    
                _log.LogDebug($"BRAKE TEMPS - " +
                    $"LF:{t.BrakeTemp[0]:F1}°{(displayUnits == 0 ? "F" : "C")}, " +
                    $"RF:{t.BrakeTemp[1]:F1}°{(displayUnits == 0 ? "F" : "C")}, " +
                    $"LR:{t.BrakeTemp[2]:F1}°{(displayUnits == 0 ? "F" : "C")}, " +
                    $"RR:{t.BrakeTemp[3]:F1}°{(displayUnits == 0 ? "F" : "C")}");
            }
        }

        // 🚨 NOVA FUNÇÃO: Popular lap deltas críticos do SDK
        private void PopulateLapDeltas(IRacingSdkData d, TelemetryModel t)
        {
            // 🏁 LAP DELTAS conforme SDK oficial (segundos)
            t.LapDeltaToBestLap = GetSdkValue<float>(d, "LapDeltaToBestLap") ?? 0f;
            t.LapDeltaToSessionBestLap = GetSdkValue<float>(d, "LapDeltaToSessionBestLap") ?? 0f;
            t.LapDeltaToSessionOptimalLap = GetSdkValue<float>(d, "LapDeltaToSessionOptimalLap") ?? 0f;
            t.LapDeltaToDriverBestLap = GetSdkValue<float>(d, "LapDeltaToOptimalLap") ?? 0f; // Optimal lap do próprio piloto
            
            // 🏁 LAP TIMES básicos (segundos)
            t.LapCurrentLapTime = GetSdkValue<float>(d, "LapCurrentLapTime") ?? 0f;
            t.LapLastLapTime = GetSdkValue<float>(d, "LapLastLapTime") ?? 0f;
            t.LapBestLapTime = GetSdkValue<float>(d, "LapBestLapTime") ?? 0f;
            t.LapDistPct = GetSdkValue<float>(d, "LapDistPct") ?? 0f;
            t.Lap = GetSdkValue<int>(d, "Lap") ?? 0;
            
            // 📊 LOG para validação de lap data
            if (_log.IsEnabled(LogLevel.Debug))
            {
                _log.LogDebug($"LAP DELTAS - " +
                    $"ToBest: {t.LapDeltaToBestLap:+0.000;-0.000;0.000}s, " +
                    $"ToSessionBest: {t.LapDeltaToSessionBestLap:+0.000;-0.000;0.000}s, " +
                    $"ToSessionOptimal: {t.LapDeltaToSessionOptimalLap:+0.000;-0.000;0.000}s, " +
                    $"ToOptimal: {t.LapDeltaToDriverBestLap:+0.000;-0.000;0.000}s");
                    
                _log.LogDebug($"LAP TIMES - " +
                    $"Current: {FormatLapTime(t.LapCurrentLapTime)}, " +
                    $"Last: {FormatLapTime(t.LapLastLapTime)}, " +
                    $"Best: {FormatLapTime(t.LapBestLapTime)}, " +
                    $"Progress: {t.LapDistPct:P1}");
            }
        }

        // 🚨 UTILITÁRIO: Formatar tempo de volta para logs
        private static string FormatLapTime(float seconds)
        {
            if (seconds <= 0) return "--:--.---";
            var ts = TimeSpan.FromSeconds(seconds);
            return $"{ts.Minutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds:D3}";
        }

        private void PopulateTyres(IRacingSdkData d, TelemetryModel t)
        {
            // 🌡️ TEMPERATURAS DOS PNEUS com conversão baseada em DisplayUnits
            int displayUnits = t.Session.DisplayUnits;
            
            t.Tyres.LfTempCl = ConvertTemperature(GetSdkValue<float>(d, "LFtempCL") ?? 0f, displayUnits);
            t.Tyres.LfTempCm = ConvertTemperature(GetSdkValue<float>(d, "LFtempCM") ?? 0f, displayUnits);
            t.Tyres.LfTempCr = ConvertTemperature(GetSdkValue<float>(d, "LFtempCR") ?? 0f, displayUnits);
            t.Tyres.RfTempCl = ConvertTemperature(GetSdkValue<float>(d, "RFtempCL") ?? 0f, displayUnits);
            t.Tyres.RfTempCm = ConvertTemperature(GetSdkValue<float>(d, "RFtempCM") ?? 0f, displayUnits);
            t.Tyres.RfTempCr = ConvertTemperature(GetSdkValue<float>(d, "RFtempCR") ?? 0f, displayUnits);
            t.Tyres.LrTempCl = ConvertTemperature(GetSdkValue<float>(d, "LRtempCL") ?? 0f, displayUnits);
            t.Tyres.LrTempCm = ConvertTemperature(GetSdkValue<float>(d, "LRtempCM") ?? 0f, displayUnits);
            t.Tyres.LrTempCr = ConvertTemperature(GetSdkValue<float>(d, "LRtempCR") ?? 0f, displayUnits);
            t.Tyres.RrTempCl = ConvertTemperature(GetSdkValue<float>(d, "RRtempCL") ?? 0f, displayUnits);
            t.Tyres.RrTempCm = ConvertTemperature(GetSdkValue<float>(d, "RRtempCM") ?? 0f, displayUnits);
            t.Tyres.RrTempCr = ConvertTemperature(GetSdkValue<float>(d, "RRtempCR") ?? 0f, displayUnits);

            // 🔧 PRESSÕES DOS PNEUS (usando conversão corrigida)
            // Cold pressures from the car setup (kPa)
            float? lfColdKpa = GetSdkValue<float>(d, "LFcoldPressure");
            float? rfColdKpa = GetSdkValue<float>(d, "RFcoldPressure");
            float? lrColdKpa = GetSdkValue<float>(d, "LRcoldPressure");
            float? rrColdKpa = GetSdkValue<float>(d, "RRcoldPressure");

            // Current hot pressures reported by the SDK (kPa)
            float? lfHotKpa = GetSdkValue<float>(d, "LFhotPressure");
            float? rfHotKpa = GetSdkValue<float>(d, "RFhotPressure");
            float? lrHotKpa = GetSdkValue<float>(d, "LRhotPressure");
            float? rrHotKpa = GetSdkValue<float>(d, "RRhotPressure");

            // Current tire pressures from telemetry (kPa)
            float? lfKpa = GetSdkValue<float>(d, "LFpress");
            float? rfKpa = GetSdkValue<float>(d, "RFpress");
            float? lrKpa = GetSdkValue<float>(d, "LRpress");
            float? rrKpa = GetSdkValue<float>(d, "RRpress");

            bool onPitRoad = t.OnPitRoad;

            void ApplyPressures(
                float? cold, float? hot, float? live,
                ref float coldField, ref float hotField, ref float liveField,
                ref float lastHot)
            {
                // 🔧 Usando conversão CORRIGIDA kPa→PSI
                if (cold.HasValue)
                    coldField = KPaToPsi(cold.Value);

                // Prefer hot values from the SDK, falling back to the last
                // recorded entry when missing (service started mid-run).
                hotField = hot.HasValue
                    ? KPaToPsi(hot.Value)
                    : (lastHot > 0f ? lastHot : 0f);

                // Use live pressure when available, otherwise fall back to the
                // known cold pressure so the UI always has a sensible value.
                liveField = live.HasValue
                    ? KPaToPsi(live.Value)
                    : (cold.HasValue ? coldField : liveField);
            }

            float lfColdPress  = t.Tyres.LfColdPress;
            float lfHotPress   = t.Tyres.LfHotPressure;
            float lfPress      = t.Tyres.LfPress;
            ApplyPressures(lfColdKpa, lfHotKpa, lfKpa,
                ref lfColdPress,
                ref lfHotPress,
                ref lfPress,
                ref _lfLastHotPress);
            t.Tyres.LfColdPress   = lfColdPress;
            t.Tyres.LfHotPressure = lfHotPress;
            t.Tyres.LfPress       = lfPress;

            float rfColdPress  = t.Tyres.RfColdPress;
            float rfHotPress   = t.Tyres.RfHotPressure;
            float rfPress      = t.Tyres.RfPress;
            ApplyPressures(rfColdKpa, rfHotKpa, rfKpa,
                ref rfColdPress,
                ref rfHotPress,
                ref rfPress,
                ref _rfLastHotPress);
            t.Tyres.RfColdPress   = rfColdPress;
            t.Tyres.RfHotPressure = rfHotPress;
            t.Tyres.RfPress       = rfPress;

            float lrColdPress  = t.Tyres.LrColdPress;
            float lrHotPress   = t.Tyres.LrHotPressure;
            float lrPress      = t.Tyres.LrPress;
            ApplyPressures(lrColdKpa, lrHotKpa, lrKpa,
                ref lrColdPress,
                ref lrHotPress,
                ref lrPress,
                ref _lrLastHotPress);
            t.Tyres.LrColdPress   = lrColdPress;
            t.Tyres.LrHotPressure = lrHotPress;
            t.Tyres.LrPress       = lrPress;

            float rrColdPress  = t.Tyres.RrColdPress;
            float rrHotPress   = t.Tyres.RrHotPressure;
            float rrPress      = t.Tyres.RrPress;
            ApplyPressures(rrColdKpa, rrHotKpa, rrKpa,
                ref rrColdPress,
                ref rrHotPress,
                ref rrPress,
                ref _rrLastHotPress);
            t.Tyres.RrColdPress   = rrColdPress;
            t.Tyres.RrHotPressure = rrHotPress;
            t.Tyres.RrPress       = rrPress;

            // 📊 LOGGING PARA VALIDAÇÃO
            if (_log.IsEnabled(LogLevel.Debug))
            {
                _log.LogDebug($"PRESSURE DEBUG - " +
                    $"LF: {t.Tyres.LfPress:F1} PSI ({lfKpa:F1} kPa), " +
                    $"RF: {t.Tyres.RfPress:F1} PSI ({rfKpa:F1} kPa), " +
                    $"LR: {t.Tyres.LrPress:F1} PSI ({lrKpa:F1} kPa), " +
                    $"RR: {t.Tyres.RrPress:F1} PSI ({rrKpa:F1} kPa)");
                    
                // 🚨 ALERTA se pressões estão fora da faixa normal
                var pressures = new[] { t.Tyres.LfPress, t.Tyres.RfPress, t.Tyres.LrPress, t.Tyres.RrPress };
                var tireNames = new[] { "LF", "RF", "LR", "RR" };
                
                for (int i = 0; i < pressures.Length; i++)
                {
                    var pressure = pressures[i];
                    if (pressure > 100f)
                    {
                        _log.LogWarning($"⚠️ Pressão {tireNames[i]} suspeita: {pressure:F1} PSI - possível erro de conversão!");
                    }
                    else if (pressure > 0f && pressure < 10f)
                    {
                        _log.LogWarning($"⚠️ Pressão {tireNames[i]} muito baixa: {pressure:F1} PSI");
                    }
                }
            }

            if (!lfColdKpa.HasValue)
            {
                if (_log.IsEnabled(LogLevel.Debug))
                    _log.LogDebug("Cold tire pressure data not available for this car (LFcoldPressure missing).");
            }

            // 🔧 DESGASTE DOS PNEUS (SEMPRE disponível conforme SDK oficial)
            // Correção: O SDK fornece dados de desgaste sempre, não só no pit road
            t.Tyres.LfWear = new float?[] {
                GetSdkValue<float>(d, "LFWearL"),
                GetSdkValue<float>(d, "LFWearM"),
                GetSdkValue<float>(d, "LFWearR")
            }.Select(v => v ?? 0f).ToArray();
            
            t.Tyres.RfWear = new float?[] {
                GetSdkValue<float>(d, "RFWearL"),
                GetSdkValue<float>(d, "RFWearM"),
                GetSdkValue<float>(d, "RFWearR")
            }.Select(v => v ?? 0f).ToArray();
            
            t.Tyres.LrWear = new float?[] {
                GetSdkValue<float>(d, "LRWearL"),
                GetSdkValue<float>(d, "LRWearM"),
                GetSdkValue<float>(d, "LRWearR")
            }.Select(v => v ?? 0f).ToArray();
            
            t.Tyres.RrWear = new float?[] {
                GetSdkValue<float>(d, "RRWearL"),
                GetSdkValue<float>(d, "RRWearM"),
                GetSdkValue<float>(d, "RRWearR")
            }.Select(v => v ?? 0f).ToArray();
            
            // 📊 CÁLCULO: Médias de desgaste por pneu (% de desgaste 0-100)
            t.Tyres.LfWearAvg = t.Tyres.LfWear.Length > 0 ? t.Tyres.LfWear.Average() : 0f;
            t.Tyres.RfWearAvg = t.Tyres.RfWear.Length > 0 ? t.Tyres.RfWear.Average() : 0f;
            t.Tyres.LrWearAvg = t.Tyres.LrWear.Length > 0 ? t.Tyres.LrWear.Average() : 0f;
            t.Tyres.RrWearAvg = t.Tyres.RrWear.Length > 0 ? t.Tyres.RrWear.Average() : 0f;
            
            // 📊 LOG para validação de desgaste
            if (_log.IsEnabled(LogLevel.Debug))
            {
                _log.LogDebug($"TYRE WEAR - " +
                    $"LF: [{string.Join(",", t.Tyres.LfWear.Select(w => $"{w:F1}%"))}] Avg:{t.Tyres.LfWearAvg:F1}%, " +
                    $"RF: [{string.Join(",", t.Tyres.RfWear.Select(w => $"{w:F1}%"))}] Avg:{t.Tyres.RfWearAvg:F1}%, " +
                    $"LR: [{string.Join(",", t.Tyres.LrWear.Select(w => $"{w:F1}%"))}] Avg:{t.Tyres.LrWearAvg:F1}%, " +
                    $"RR: [{string.Join(",", t.Tyres.RrWear.Select(w => $"{w:F1}%"))}] Avg:{t.Tyres.RrWearAvg:F1}%");
            }
            
            // 🔧 CORREÇÃO FUTURA: UpdateLastHotPressures será adicionada aqui
            // UpdateLastHotPressures(t);
        }

        private async Task ApplyYamlData(IRacingSdkData d, TelemetryModel t)
        {
            t.SessionInfoYaml = _reader.Data?.SessionInfoYaml ?? string.Empty;
            if (!string.IsNullOrEmpty(t.SessionInfoYaml) && t.SessionInfoYaml != _lastYaml)
            {
                if (_log.IsEnabled(LogLevel.Debug))
                    _log.LogDebug($"Atualizando cache do YAML. PlayerCarIdx: {t.PlayerCarIdx}, SessionNum: {t.SessionNum}");
                _cachedYamlData = _yamlParser.ParseSessionInfo(
                    t.SessionInfoYaml,
                    t.PlayerCarIdx,
                    t.SessionNum,
                    t.Session.SessionUniqueID
                );
                LogYamlDump(t.SessionInfoYaml);
                _lastYaml = t.SessionInfoYaml;
            }

            var (drv, wkd, ses, sec, drivers) = _cachedYamlData;
            t.YamlPlayerDriver = drv;
            t.YamlWeekendInfo  = wkd;
            t.YamlSessionInfo  = ses;
            t.YamlSectorInfo   = sec;
            t.YamlDrivers      = drivers;
            
            if (drv != null)
            {
                t.UserName           = drv.UserName;
                t.TeamName           = drv.TeamName;
                t.CarNumber          = drv.CarNumber;
                t.IRating            = drv.IRating;
                t.LicString          = drv.LicString;
                t.LicSafetyRating    = drv.LicLevel + drv.LicSubLevel / 1000f;
                t.PlayerCarClassID   = drv.CarClassID;
                t.TireCompound       = drv.TireCompound;
                t.Tyres.Compound     = drv.TireCompound;
            }

            // 🔧 PARSING DE PRESSÕES DO YAML (com conversão corrigida)
            if (t.Vehicle.PlayerCarPitStopCount > _lastPitCount && !string.IsNullOrEmpty(t.SessionInfoYaml))
            {
                _lastPitCount = t.Vehicle.PlayerCarPitStopCount;
                try
                {
                    using var reader = new StringReader(t.SessionInfoYaml);
                    var yamlStream = new YamlStream();
                    yamlStream.Load(reader);
                    var root = (YamlMappingNode)yamlStream.Documents[0].RootNode;
                    if (root.Children.TryGetValue(new YamlScalarNode("CarSetup"), out var csNode) && csNode is YamlMappingNode csMap)
                    {
                        if (csMap.Children.TryGetValue(new YamlScalarNode("Tires"), out var tiresNode) && tiresNode is YamlMappingNode tires)
                        {
                            float ParsePressure(YamlMappingNode n, string field)
                            {
                                string val = GetStr(n, field);
                                if (string.IsNullOrEmpty(val)) return 0f;
                                val = val.Replace(" kPa", string.Empty).Trim();
                                if (!float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v))
                                    return 0f;
                                // 🔧 Usando conversão CORRIGIDA se valor está em kPa
                                return val.Contains("kPa") || v > 100f ? KPaToPsi(v) : v;
                            }
                            
                            // ... resto do parsing YAML
                        }
                    }
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Failed to parse LastHotPressure from session YAML.");
                }
            }
            
            // ... resto do método
        }

        private string GetStr(YamlMappingNode n, string key)
        {
            if (n.Children.TryGetValue(new YamlScalarNode(key), out var v) && v is YamlScalarNode s)
            {
                return s.Value ?? string.Empty;
            }
            return string.Empty;
        }

        private void LogYamlDump(string yaml)
        {
            if (_log.IsEnabled(LogLevel.Trace))
            {
                _log.LogTrace("YAML atualizado: {Yaml}", yaml);
            }
        }
    }
}