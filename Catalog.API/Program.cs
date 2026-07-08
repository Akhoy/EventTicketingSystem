using Catalog.Features.GetItems;
using Catalog.Features.Seed;
using Catalog.Features.SyncBooking;
using Catalog.Shared;
using MongoDB.Driver;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

var mongoConnectionString = builder.Configuration.GetConnectionString("MongoDb");
var mongoClient = new MongoClient(mongoConnectionString);
var database = mongoClient.GetDatabase("CatalogDb");
builder.Services.AddSingleton<IMongoDatabase>(database);

var redisConnectionString = builder.Configuration.GetConnectionString("Redis");
var redis = ConnectionMultiplexer.Connect(redisConnectionString!);
builder.Services.AddSingleton<IConnectionMultiplexer>(redis);

builder.Services.AddScoped<ICatalogRepository, CatalogRepository>();
builder.Services.AddHostedService<SyncBookingWorker>();

var app = builder.Build();

await CatalogSeeder.SeedAsync(app);
GetItemsEndpoint.Register(app);

app.Run();
