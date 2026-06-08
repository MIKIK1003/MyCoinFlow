using MyCoinFlow.Services;

namespace MyCoinFlow.CourseWorker
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;

        public Worker(ILogger<Worker> logger)
        {
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("MyCoinFlow CourseWorker gestartet: {time}", DateTimeOffset.Now);

            try
            {
                var service = new VermoegenKursUpdateService();
                var result = await service.AktualisierenAsync();

                _logger.LogInformation("{message}", result.Meldung);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Kursaktualisierung fehlgeschlagen.");
            }

            _logger.LogInformation("MyCoinFlow CourseWorker beendet: {time}", DateTimeOffset.Now);
        }
    }
}