using System.Diagnostics.Metrics;
using AutoMapper;
using Catalog.Bll.Caching;
using Catalog.Bll.Dtos;
using Catalog.Bll.Mapping;
using Catalog.Bll.Services;
using Catalog.Dal.Abstractions;
using Catalog.Domain.Entities;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Catalog.Tests;

/// <summary>Дворівневий кеш (Лаб. 06): L1 → L2 → БД, інвалідація при записі, робота без Redis.</summary>
public class CachingTests
{
    private static IMapper Mapper() =>
        new MapperConfiguration(cfg => cfg.AddMaps(typeof(MovieProfile).Assembly), NullLoggerFactory.Instance).CreateMapper();

    private static (IUnitOfWork Uow, GenreService Service) CreateGenres(ITwoLevelCache cache)
    {
        var uow = Substitute.For<IUnitOfWork>();
        uow.Genres.Returns(Substitute.For<IGenreRepository>());
        uow.Genres.GetAllAsync(Arg.Any<CancellationToken>()).Returns([new Genre("Драма"), new Genre("Комедія")]);
        return (uow, new GenreService(uow, Mapper(), cache, NullLogger<GenreService>.Instance));
    }

    [Fact]
    public async Task Second_read_is_served_from_cache_without_database()
    {
        var (uow, service) = CreateGenres(TestCache.Create());
        var ct = TestContext.Current.CancellationToken;

        var first = await service.GetAllAsync(ct);
        var second = await service.GetAllAsync(ct);

        Assert.Equal(first.Select(g => g.Name), second.Select(g => g.Name));
        await uow.Genres.Received(1).GetAllAsync(Arg.Any<CancellationToken>());   // БД — лише перший раз
    }

    [Fact]
    public async Task Write_invalidates_cache_so_next_read_goes_to_database()
    {
        var (uow, service) = CreateGenres(TestCache.Create());
        var ct = TestContext.Current.CancellationToken;
        await service.GetAllAsync(ct);

        await service.CreateAsync(new CreateGenreDto { Name = "Фентезі" }, ct);
        await service.GetAllAsync(ct);

        await uow.Genres.Received(2).GetAllAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Second_replica_gets_value_from_shared_L2_not_from_database()
    {
        var redis = TestCache.SharedRedis();
        var metrics = TestCache.Metrics();
        var replica1 = TestCache.Create(redis, metrics);
        var replica2 = TestCache.Create(redis, metrics);   // свій L1, спільний L2
        var dbCalls = 0;
        Task<GenreDto> Factory(CancellationToken _) { dbCalls++; return Task.FromResult(new GenreDto { Id = 1, Name = "Драма" }); }
        var ct = TestContext.Current.CancellationToken;

        await replica1.GetOrCreateAsync("catalog:test:1", CachePolicy.Genres, Factory, ct);
        var fromL2 = await replica2.GetOrCreateAsync("catalog:test:1", CachePolicy.Genres, Factory, ct);

        Assert.Equal("Драма", fromL2.Name);
        Assert.Equal(1, dbCalls);
        Assert.Equal(1L, metrics.Snapshot()["l2.hits"]);
    }

    [Fact]
    public async Task Unavailable_redis_does_not_break_reads()
    {
        var broken = Substitute.For<IDistributedCache>();
        broken.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("No connection is available"));
        var metrics = TestCache.Metrics();
        var cache = TestCache.Create(broken, metrics);
        var ct = TestContext.Current.CancellationToken;

        var value = await cache.GetOrCreateAsync("catalog:test:2", CachePolicy.MovieCard,
            _ => Task.FromResult(new GenreDto { Id = 2, Name = "Комедія" }), ct);

        Assert.Equal("Комедія", value.Name);              // відповідь з БД, а не виняток
        Assert.Equal(1L, metrics.Snapshot()["errors"]);
    }

    [Fact]
    public void L1_ttl_is_always_shorter_than_L2()
    {
        foreach (var policy in new[] { CachePolicy.MovieCard, CachePolicy.Genres, CachePolicy.MovieList, CachePolicy.Version })
            Assert.True(policy.L1Absolute < policy.L2Absolute);
    }
}

internal static class TestCache
{
    public static IDistributedCache SharedRedis() =>
        new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

    public static CacheMetrics Metrics() =>
        new(new ServiceCollection().AddMetrics().BuildServiceProvider().GetRequiredService<IMeterFactory>());

    public static TwoLevelCache Create(IDistributedCache? redis = null, CacheMetrics? metrics = null) =>
        new(new MemoryCache(new MemoryCacheOptions { SizeLimit = 1_000 }), redis ?? SharedRedis(), metrics ?? Metrics(),
            NullLogger<TwoLevelCache>.Instance);
}
