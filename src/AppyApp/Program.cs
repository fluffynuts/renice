// just basically stops and waits for any
// input, so tests can manipulate priority
// and then shut down the process
Console.WriteLine("zzz");
var waitString = args.FirstOrDefault(s => int.TryParse(s, out _))
    ?? "10000";
var wait = int.Parse(waitString);
Thread.Sleep(wait);