using LocalSonar;

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

return await LocalSonarApp.RunAsync(args, cts.Token);
