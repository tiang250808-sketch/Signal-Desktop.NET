using System;
using CPF.Linux;
using CPF.Mac;
using CPF.Platform;
using CPF.Skia;
using CPF.Windows;
using Microsoft.Extensions.DependencyInjection;
using SignalCpf.Client;
using SignalCpf.Core.Abstractions;
using SignalCpf.Core.Options;
using SignalCpf.Net.Http;
using SignalCpf.Storage;
using SignalCpf.UI.ViewModels;

namespace SignalCpf.UI;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.Initialize(
            (OperatingSystemType.Windows, new WindowsPlatform(), new SkiaDrawingFactory { UseGPU = true }),
            (OperatingSystemType.OSX, new MacPlatform(), new SkiaDrawingFactory { UseGPU = false }),
            (OperatingSystemType.Linux, new LinuxPlatform(), new SkiaDrawingFactory { UseGPU = true }));

        var services = BuildServices();
        try
        {
            var vm = services.GetRequiredService<MainViewModel>();
            var window = new MainWindow(vm);
            Application.Run(window);
        }
        finally
        {
            // Orchestrator may already be disposed by MainWindow shutdown; second dispose is no-op.
            try
            {
                services.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(3));
            }
            catch
            {
                // ignore shutdown races
            }
        }
    }

    private static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton(SignalServerOptions.FromEnvironment());
        services.AddSingleton<ICredentialStore, CredentialStore>();
        services.AddSingleton<IMessageStore, SqliteMessageStore>();
        services.AddSingleton<SignalRestClient>();
        services.AddSingleton<ISignalSidecarClient, SignalClientOrchestrator>();
        services.AddSingleton<MainViewModel>();
        return services.BuildServiceProvider();
    }
}
