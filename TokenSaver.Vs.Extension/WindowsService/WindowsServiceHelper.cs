using System;
using System.ServiceProcess;
using System.Threading.Tasks;

namespace TokenSaver.Vs.Extension.WindowsService;

/// <summary>Thin wrapper over System.ServiceProcess.ServiceController for the TokenSaverMCP SCM service.</summary>
public static class WindowsServiceHelper
{
    public static Task<bool> IsServiceInstalledAsync(string serviceName)
    {
        return Task.Run(() =>
        {
            try
            {
                using var controller = new ServiceController(serviceName);
                _ = controller.Status;
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        });
    }

    public static Task StartServiceAsync(string serviceName, TimeSpan? timeout = null)
    {
        return Task.Run(() =>
        {
            using var controller = new ServiceController(serviceName);
            if (controller.Status is ServiceControllerStatus.Running or ServiceControllerStatus.StartPending)
            {
                return;
            }

            controller.Start();
            controller.WaitForStatus(ServiceControllerStatus.Running, timeout ?? TimeSpan.FromSeconds(30));
        });
    }

    public static Task StopServiceAsync(string serviceName, TimeSpan? timeout = null)
    {
        return Task.Run(() =>
        {
            using var controller = new ServiceController(serviceName);
            if (controller.Status is ServiceControllerStatus.Stopped or ServiceControllerStatus.StopPending)
            {
                return;
            }

            controller.Stop();
            controller.WaitForStatus(ServiceControllerStatus.Stopped, timeout ?? TimeSpan.FromSeconds(30));
        });
    }
}
