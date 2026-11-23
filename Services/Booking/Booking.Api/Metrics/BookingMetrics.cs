using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Booking.Api.Metrics;

/// <summary>
/// Бізнес-метрики бронювання (Meter "Booking.Api", Singleton; Meter додано в ServiceDefaults → вкладка Metrics у Dashboard).
/// <list type="bullet">
/// <item><c>bookings.created</c> {booking} — створені бронювання, тег booking.status;</item>
/// <item><c>bookings.paid</c> {booking} — оплати, тег payment.method (card | cash | online);</item>
/// <item><c>bookings.cancelled</c>, <c>bookings.checked_in</c> {booking} — скасування й вхід до зали;</item>
/// <item><c>tickets.sold</c> {ticket} — квитки у створених бронюваннях;</item>
/// <item><c>bookings.operation.duration</c> ms — тривалість операцій (operation, status = success | failure) для ВСІХ виконань;</item>
/// <item><c>sessions.upcoming</c> {session} — gauge: майбутні сеанси (знімок, оновлює <see cref="BookingSnapshotService"/>);</item>
/// <item><c>bookings.operations.in_flight</c> {operation} — gauge: операції запису, що виконуються зараз.</item>
/// </list>
/// Теги мають кілька фіксованих значень; Id бронювань, глядачів і сеансів у теги не потрапляють (низька кардинальність).
/// </summary>
public sealed class BookingMetrics
{
    public const string MeterName = "Booking.Api";

    private readonly Counter<long> _created;
    private readonly Counter<long> _paid;
    private readonly Counter<long> _cancelled;
    private readonly Counter<long> _checkedIn;
    private readonly Counter<long> _ticketsSold;
    private readonly Histogram<double> _duration;
    private long _inFlight;
    private int _upcomingSessions;

    public BookingMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);
        _created = meter.CreateCounter<long>("bookings.created", unit: "{booking}", description: "Створені бронювання");
        _paid = meter.CreateCounter<long>("bookings.paid", unit: "{booking}", description: "Оплачені бронювання");
        _cancelled = meter.CreateCounter<long>("bookings.cancelled", unit: "{booking}", description: "Скасовані бронювання");
        _checkedIn = meter.CreateCounter<long>("bookings.checked_in", unit: "{booking}", description: "Глядачі, що увійшли до зали");
        _ticketsSold = meter.CreateCounter<long>("tickets.sold", unit: "{ticket}", description: "Квитки у створених бронюваннях");
        _duration = meter.CreateHistogram<double>("bookings.operation.duration", unit: "ms", description: "Тривалість операцій бронювання");
        meter.CreateObservableGauge("sessions.upcoming", () => Volatile.Read(ref _upcomingSessions),
            unit: "{session}", description: "Майбутні сеанси (знімок)");
        meter.CreateObservableGauge("bookings.operations.in_flight", () => Interlocked.Read(ref _inFlight),
            unit: "{operation}", description: "Операції бронювання, що виконуються зараз");
    }

    public void BookingCreated(string status, int tickets)
    {
        _created.Add(1, new KeyValuePair<string, object?>("booking.status", status));
        _ticketsSold.Add(tickets);
    }

    public void BookingPaid(string method) => _paid.Add(1, new KeyValuePair<string, object?>("payment.method", method));
    public void BookingCancelled() => _cancelled.Add(1);
    public void BookingCheckedIn() => _checkedIn.Add(1);

    public void SetUpcomingSessions(int count) => Volatile.Write(ref _upcomingSessions, count);

    /// <summary>Вимірює операцію: тривалість записується і для успіху, і для винятку (тег status).</summary>
    public async Task<T> MeasureAsync<T>(string operation, Func<Task<T>> action)
    {
        Interlocked.Increment(ref _inFlight);
        var sw = Stopwatch.StartNew();
        var success = false;
        try
        {
            var result = await action();
            success = true;
            return result;
        }
        finally
        {
            Interlocked.Decrement(ref _inFlight);
            _duration.Record(sw.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("operation", operation),
                new KeyValuePair<string, object?>("status", success ? "success" : "failure"));
        }
    }

    public Task MeasureAsync(string operation, Func<Task> action) =>
        MeasureAsync(operation, async () => { await action(); return true; });
}
