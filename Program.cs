using Microsoft.Extensions.Hosting;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication((_, builder) => { })
    .Build();

host.Run();
