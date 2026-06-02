// Courtly Worker host. Scaffold stub — the RabbitMQ consumer and email/notification handlers arrive in feature 17.
var builder = Host.CreateApplicationBuilder(args);

var host = builder.Build();

host.Run();
