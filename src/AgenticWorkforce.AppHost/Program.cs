var builder = DistributedApplication.CreateBuilder(args);

// -- Infrastructure --
var postgres = builder.AddPostgres("postgres")
    .WithDataVolume()
    .WithPgAdmin()
    .AddDatabase("agenticworkforce");

var redis = builder.AddRedis("redis")
    .WithDataVolume();

// -- BFF API --
var api = builder.AddProject<Projects.AgenticWorkforce_Api>("api")
    .WithReference(postgres)
    .WithReference(redis)
    .WithExternalHttpEndpoints();

// -- Background Worker --
// Restore when Phase 6 lands the agent runtime + Durable Task hosted
// services. Until then the Worker process is a no-op that consumes a
// Postgres connection and a Redis multiplexer for nothing.
// builder.AddProject<Projects.AgenticWorkforce_Worker>("worker")
//     .WithReference(postgres)
//     .WithReference(redis);

builder.Build().Run();
