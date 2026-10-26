using AutoMapper;
using Booking.Bll.Mapping;
using Microsoft.Extensions.Logging.Abstractions;

namespace Booking.Tests;

public class MappingConfigurationTests
{
    [Fact]
    public void AutoMapper_configuration_is_valid()
    {
        var config = new MapperConfiguration(cfg => cfg.AddMaps(typeof(BookingProfile).Assembly), NullLoggerFactory.Instance);
        config.AssertConfigurationIsValid();
    }
}
