using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Diagnostics;

namespace console
{
    class Program
    {
		static ConcurrentBag<int> oldBag = new ConcurrentBag<int>();
		static ConcurrentBag<int> newBag = new ConcurrentBag<int>();

		static async Task CallAPI(HttpClient client, string url)
		{
			try
			{
				var res = await client.GetAsync(url);
				var c = await res.Content.ReadAsStringAsync();
				if (c.Contains("NEW"))
				{
					newBag.Add(1);
				}
				else
				{
					oldBag.Add(1);
				}
			}
			catch(Exception e)
			{
				Console.WriteLine(e.ToString());
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

        static void Main(string[] args)
        {
			Console.WriteLine("Starting test");
			var timer = new Stopwatch();
			timer.Start();

			LoadTest(args[0], 1000);
			timer.Stop();

			var total = newBag.Count + oldBag.Count;
			var percNew = ((double)newBag.Count / total) * 100;
			Console.WriteLine($"Percentage onto new version: {percNew}%");
			Console.WriteLine($"Time: {timer.ElapsedMilliseconds} ms");
			Console.ReadLine();
		}
	}
}
