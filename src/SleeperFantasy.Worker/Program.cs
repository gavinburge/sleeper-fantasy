using SleeperFantasy.Worker;
using SleeperFantasy.Infrastructure;

var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddCommandLine(args);
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
