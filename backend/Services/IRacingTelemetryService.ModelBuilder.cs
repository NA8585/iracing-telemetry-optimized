using System.Threading;
using System.Threading.Tasks;
using SuperBackendNR85IA.Models;
using SuperBackendNR85IA.Calculations;

namespace SuperBackendNR85IA.Services
{
    public sealed partial class IRacingTelemetryService
    {
        private async Task<TelemetryModel?> BuildTelemetryModelAsync(CancellationToken ct)
        {
            if (_reader.Data == null) return null;

            var d = _reader.Data;
            var t = new TelemetryModel();
            using var perf = new Utilities.PerformanceMonitor(_log, "BuildTelemetry");

            PopulateSessionInfo(d, t); // PRIMEIRO para ter DisplayUnits disponível
            PopulateVehicleData(d, t);  // SEGUNDO para usar DisplayUnits nas conversões
            PopulatePhysicsData(d, t);  // TERCEIRO para forças G, ângulos e temperaturas ambientais
            PopulateBrakeData(d, t);    // QUARTO para pressões e temperaturas de freio
            PopulateLapDeltas(d, t);    // QUINTO para lap deltas críticos
            PopulateTyres(d, t);        // SEXTO para usar DisplayUnits nas temperaturas dos pneus
            PopulateAllExtraData(d, t);
            UpdateLapInfo(d, t);
            ReadSectorTimes(d, t);
            ComputeForceFeedback(d, t);
            ComputeRelativeDistances(d, t);
            if (_log.IsEnabled(LogLevel.Debug))
            {
                _log.LogDebug(
                    $"Tyre snapshot - Pressures LF:{t.LfPress} RF:{t.RfPress} LR:{t.LrPress} RR:{t.RrPress}, " +
                    $"HotPress LF:{_lfLastHotPress} RF:{_rfLastHotPress} LR:{_lrLastHotPress} RR:{_rrLastHotPress}, " +
                    $"ColdPress LF:{_lfColdPress} RF:{_rfColdPress} LR:{_lrColdPress} RR:{_rrColdPress}, " +
                    $"Temps LF:{t.LfTempCl}/{t.LfTempCm}/{t.LfTempCr} RF:{t.RfTempCl}/{t.RfTempCm}/{t.RfTempCr} " +
                    $"LR:{t.LrTempCl}/{t.LrTempCm}/{t.LrTempCr} RR:{t.RrTempCl}/{t.RrTempCm}/{t.RrTempCr}, " +
                    $"Tread FL:{t.TreadRemainingFl} FR:{t.TreadRemainingFr} RL:{t.TreadRemainingRl} RR:{t.TreadRemainingRr}");
            }
            UpdateLastHotPress(t);
            await ApplyYamlData(d, t);
            RunCustomCalculations(d, t);
            ValidateDataRanges(t); // 🚨 CRÍTICO: Validar ranges de dados
            TelemetryCalculations.SanitizeModel(t);
            await PersistCarTrackData(t);

            return t;
        }
    }
}
