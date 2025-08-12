using LimpiadorPlayas_Prueba1;
using LimpiadorPlayas_Prueba1.Servicios;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");

builder.Services.AddScoped<EstadoJuego>();

await builder.Build().RunAsync();
