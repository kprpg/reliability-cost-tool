var builder = DistributedApplication.CreateBuilder(args);

var api = builder.AddProject<Projects.ReliabilityCostTool_Api>("api")
    .WithHttpHealthCheck("/health");

builder.AddProject<Projects.ReliabilityCostTool_Web>("web")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WaitFor(api);

builder.Build().Run();
