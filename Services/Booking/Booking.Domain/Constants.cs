namespace Booking.Domain;

/// <summary>Статуси бронювання (CHECK CK_Bookings_Status у БД).</summary>
public static class BookingStatuses
{
    public const string Created = "Created";
    public const string Paid = "Paid";
    public const string Used = "Used";
    public const string Cancelled = "Cancelled";
}

public static class SeatTypes
{
    public const string Standard = "Standard";
    public const string Vip = "VIP";

    /// <summary>VIP-місце дорожче в 1.5 раза (те саме правило, що в usp_Booking_Create з Лаб. 01).</summary>
    public const decimal VipMultiplier = 1.5m;
}

public static class PaymentMethods
{
    public const string Card = "card";
    public const string Cash = "cash";
    public const string Online = "online";

    public static readonly string[] All = [Card, Cash, Online];
}
