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
        }

        private void PopulateTyres(IRacingSdkData d, TelemetryModel t)
        {
            // 🌡️ TEMPERATURAS DOS PNEUS (já funcionam corretamente)
            t.Tyres.LfTempCl = GetSdkValue<float>(d, "LFtempCL") ?? 0f;
            t.Tyres.LfTempCm = GetSdkValue<float>(d, "LFtempCM") ?? 0f;
            t.Tyres.LfTempCr = GetSdkValue<float>(d, "LFtempCR") ?? 0f;
            t.Tyres.RfTempCl = GetSdkValue<float>(d, "RFtempCL") ?? 0f;
            t.Tyres.RfTempCm = GetSdkValue<float>(d, "RFtempCM") ?? 0f;
            t.Tyres.RfTempCr = GetSdkValue<float>(d, "RFtempCR") ?? 0f;
            t.Tyres.LrTempCl = GetSdkValue<float>(d, "LRtempCL") ?? 0f;
            t.Tyres.LrTempCm = GetSdkValue<float>(d, "LRtempCM") ?? 0f;
            t.Tyres.LrTempCr = GetSdkValue<float>(d, "LRtempCR") ?? 0f;
            t.Tyres.RrTempCl = GetSdkValue<float>(d, "RRtempCL") ?? 0f;
            t.Tyres.RrTempCm = GetSdkValue<float>(d, "RRtempCM") ?? 0f;
            t.Tyres.RrTempCr = GetSdkValue<float>(d, "RRtempCR") ?? 0f;

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

            // DESGASTE DOS PNEUS (quando no pit road)
            if (onPitRoad)
            {
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