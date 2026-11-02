using System.Diagnostics;

namespace Fabric.Packs.Tests;

internal sealed class RunningHost(Process process, Uri address) : IDisposable
{
    private readonly HttpClient client = new() { BaseAddress = address };

    public async Task<HttpResponseMessage> PostAsync(string path)
    {
        await GetAsync("/alive");
        return await client.PostAsync(new Uri(path, UriKind.Relative), null);
    }

    /// <summary>Waits until /alive answers, or fails with the host's output.</summary>
    public async Task<HttpResponseMessage> GetAsync(string path)
    {
        for (var attempt = 0; attempt < 60; attempt++)
        {
            try
            {
                if ((await client.GetAsync(new Uri("/alive", UriKind.Relative))).IsSuccessStatusCode)
                {
                    return await client.GetAsync(new Uri(path, UriKind.Relative));
                }
            }
            catch (HttpRequestException)
            {
                if (process.HasExited)
                {
                    break;
                }
            }

            await Task.Delay(500);
        }

        throw new InvalidOperationException($"The host did not start:\n{await process.StandardOutput.ReadToEndAsync()}{await process.StandardError.ReadToEndAsync()}");
    }

    public void Dispose()
    {
        client.Dispose();
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
        }

        process.Dispose();
    }
}
