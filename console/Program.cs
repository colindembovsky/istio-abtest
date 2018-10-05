using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Diagnostics;
using System.Linq;

namespace console
{
    class Program
    {
		static ConcurrentDictionary<string, int> versionDict = new ConcurrentDictionary<string, int>();

		static async Task CallAPI(HttpClient client, string url)
		{
			try
			{
				var res = await client.GetAsync(url);
				var version = await res.Content.ReadAsStringAsync();
				if (versionDict.ContainsKey(version))
				{
					versionDict[version] = versionDict[version] + 1;
				}
				else
				{
					versionDict[version] = 1;
				}
			}
			catch(Exception e)
			{
				//Console.WriteLine(e.ToString());
			}
		}

        static void LoadTest(string url, int count)
        {
            var taskList = new List<Task>();
			var client = new HttpClient();
            for (var i = 0; i < count; i++)
            {
                taskList.Add(CallAPI(client, url));
            }

            Task.WaitAll(taskList.ToArray());
        }

		static void ContinualTest(string url)
		{
			var client = new HttpClient();
			Console.WriteLine("Press ESC to stop");
			do 
			{
				while (! Console.KeyAvailable)
				{
					Task.Run(async () => {
						try 
						{
							var res = await client.GetAsync(url);
							var version = await res.Content.ReadAsStringAsync();
							Console.WriteLine(version);
						}
						catch (Exception ex)
						{
							Console.WriteLine(ex.Message);
						}
					}).GetAwaiter().GetResult();
				}       
			} 
			while (Console.ReadKey(true).Key != ConsoleKey.Escape);
		}

		static void Main(string[] args)
		{
			if (args[0].ToLower() == "load")
			{
				Console.WriteLine("Starting load test");
				var timer = new Stopwatch();
				timer.Start();

				LoadTest(args[1], 1000);
				timer.Stop();
				Console.WriteLine($"Time: {timer.ElapsedMilliseconds} ms");

				var total = versionDict.Sum((x) => x.Value);
				Console.WriteLine($"Total calls: {total}");
				foreach(var v in versionDict.Keys)
				{
					Console.WriteLine($"  -- version [{v}], percentage [{((double)versionDict[v] / total) * 100}]");
				}

				Console.WriteLine($"Average call response time: {(double)timer.ElapsedMilliseconds / total} ms");
			}
			else
			{
				ContinualTest(args[1]);
			}
		}
	}
}
