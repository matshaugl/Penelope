using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Penelope.Modules.Ollama;

static class OllamaListen
{
    public const string Warning =
        "Other devices on this network will be able to use the models. Ollama has no password. Turn this off when you are finished.";

    public static string HostValue(bool exposeOnLan, int port)
    {
        return exposeOnLan ? $"0.0.0.0:{port}" : $"127.0.0.1:{port}";
    }

    public static bool IsExposed(IEnumerable<string> listenAddresses)
    {
        return listenAddresses.Any(address => address is "0.0.0.0" or "::" or "0:0:0:0:0:0:0:0");
    }

    public static IReadOnlyList<string> ShareableUrls(int port)
    {
        var urls = new List<string>();
        foreach (var address in LanAddresses())
        {
            urls.Add($"http://{address}:{port}");
        }

        return urls;
    }

    public static IReadOnlyList<string> LanAddresses()
    {
        var addresses = new List<string>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up)
            {
                continue;
            }

            foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(unicast.Address))
                {
                    continue;
                }

                var text = unicast.Address.ToString();
                if (text.StartsWith("169.254.", StringComparison.Ordinal) || addresses.Contains(text))
                {
                    continue;
                }

                addresses.Add(text);
            }
        }

        return addresses;
    }
}
