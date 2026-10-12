using System.Net;
using System.Net.Sockets;
using Penelope.Modules.Ollama;
using Xunit;

namespace Penelope.Host.Tests;

public class OllamaListenTests
{
    [Fact]
    public void Host_values_stay_on_a_known_address()
    {
        Assert.Equal("127.0.0.1:11434", OllamaListen.HostValue(false, 11434));
        Assert.Equal("0.0.0.0:11434", OllamaListen.HostValue(true, 11434));
        Assert.False(OllamaListen.IsExposed(["127.0.0.1"]));
        Assert.True(OllamaListen.IsExposed(["0.0.0.0"]));
    }

    [Fact]
    public void The_firewall_rule_is_private_and_named()
    {
        var add = LanFirewall.AddArguments(@"C:\Ollama\ollama.exe", 11434);
        Assert.Contains("name=\"Penelope Ollama LAN\"", add, StringComparison.Ordinal);
        Assert.Contains("profile=private", add, StringComparison.Ordinal);
        Assert.Contains("localport=11434", add, StringComparison.Ordinal);
        Assert.Contains(@"program=""C:\Ollama\ollama.exe""", add, StringComparison.Ordinal);
        Assert.Equal("advfirewall firewall delete rule name=\"Penelope Ollama LAN\"", LanFirewall.DeleteArguments());
    }

    [Fact]
    public void A_loopback_listener_is_not_the_network()
    {
        var tcp = new TcpListener(IPAddress.Loopback, 0);
        tcp.Start();
        var port = ((IPEndPoint)tcp.LocalEndpoint).Port;
        try
        {
            var addresses = TcpListeners.ListenAddresses(port);
            Assert.Contains("127.0.0.1", addresses);
            Assert.False(OllamaListen.IsExposed(addresses));
        }
        finally
        {
            tcp.Stop();
        }
    }
}
