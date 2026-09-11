using Atlas.Application.Notifications.Services;
using Atlas.Infrastructure;
using Atlas.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

// Only persistence — this worker never touches Blob Storage and never issues
// a JWT, so it has no reason to require BlobStorage:*/Jwt:* configuration the
// way Atlas.Api's full AddInfrastructure() would (see DependencyInjection.cs
// for the three-way split this relies on: AddPersistence / AddBlobStorage /
// AddAuthInfrastructure). It points at the exact same AtlasDb database
// Atlas.Api uses — same ConnectionStrings:AtlasDb key, same Key Vault secret
// in Azure — because it's reading and writing the same Tickets/Notifications
// tables, not a database of its own.
builder.Services.AddPersistence(builder.Configuration);

// Service Bus (Del 12) — this worker only consumes (TicketAssignedConsumer
// below), it never publishes, but AddMessaging registers the same singleton
// ServiceBusClient either side needs; there's no separate "consumer-only"
// variant since a ServiceBusClient itself doesn't distinguish sender from
// receiver, only the CreateSender/CreateProcessor call on it does.
builder.Services.AddMessaging(builder.Configuration);

builder.Services.AddScoped<IOverdueTicketNotificationService, OverdueTicketNotificationService>();

builder.Services.Configure<OverdueTicketWorkerOptions>(
    builder.Configuration.GetSection(OverdueTicketWorkerOptions.SectionName));

builder.Services.AddHostedService<OverdueTicketWorker>();
builder.Services.AddHostedService<TicketAssignedConsumer>();

var host = builder.Build();
host.Run();
