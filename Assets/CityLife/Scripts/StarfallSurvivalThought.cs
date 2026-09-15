using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CityLife.World
{
    // Bounded advisory decision. The caller supplies only currently eligible
    // action tokens, never a map of undiscovered resources or Unity objects.
    public static class StarfallSurvivalThought
    {
        // This is a separately bounded, asynchronous high-level choice, not
        // the 1500 ms immediate action/reflection deadline. Warm Nano 4B
        // emitted an actual eligible message around 3.55 s after 80 reasoning
        // tokens; the shorter cap yielded finish_reason=length without text.
        public const int DeadlineMilliseconds = 5000;
        public sealed class Result
        {
            public string status="fallback", answer="", requestJson="", responseJson="", finishReason="", model="";
            public long milliseconds;
            public string RequestHash => Starfall.Food.FoodModel.Hash(requestJson ?? "");
            public string ResponseHash => Starfall.Food.FoodModel.Hash(responseJson ?? "");
        }
        public static bool Parse(string raw, IReadOnlyCollection<string> eligible, out string action)
        {
            action=null;
            if(string.IsNullOrWhiteSpace(raw)||raw.Length>40||eligible==null)return false;
            string token=raw.Trim().ToLowerInvariant();
            foreach(string candidate in eligible)
                if(string.Equals(token,candidate,StringComparison.Ordinal)) { action=token; return true; }
            return false;
        }
        public static string BuildRequest(string model, int hunger, int thirst, IReadOnlyCollection<string> eligible)
        {
            if(eligible==null||eligible.Count==0||eligible.Count>8)throw new ArgumentException("bounded eligible actions required");
            return NpcBoundedJson.Encode(new Dictionary<string,object> {
                ["model"]=model,["stream"]=false,["temperature"]=0,["max_tokens"]=128,["reasoning_effort"]="none",
                ["messages"]=new object[] {
                    new Dictionary<string,object>{["role"]="system",["content"]="Choose exactly one listed two-word action. No explanation, JSON, coordinates, facts, or extra words. Sight and outcomes are checked by the game."},
                    new Dictionary<string,object>{["role"]="user",["content"]="Energy="+hunger+"; hydration="+thirst+". Eligible actions: "+string.Join(", ",eligible)+"."}
                }
            });
        }
        public static async Task<Result> Request(string endpoint,string model,int hunger,int thirst,
            IReadOnlyCollection<string> eligible,CancellationToken cancellation)
        {
            var result=new Result{model=model,requestJson=BuildRequest(model,hunger,thirst,eligible)};
            Uri origin=StarfallLivingMemoryClient.Loopback(endpoint);
            using(var http=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false,UseProxy=false}){Timeout=Timeout.InfiniteTimeSpan})
            using(var local=CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            {
                var timer=Stopwatch.StartNew(); Task<string> work=null;
                try
                {
                    work=StarfallLivingMemoryClient.Send(http,new HttpRequestMessage(HttpMethod.Post,new Uri(origin,"v1/chat/completions")) {
                        Content=new StringContent(result.requestJson,Encoding.UTF8,"application/json")},local.Token);
                    if(await Task.WhenAny(work,Task.Delay(DeadlineMilliseconds,local.Token)).ConfigureAwait(false)!=work)
                        result.status=cancellation.IsCancellationRequested?"cancelled":"timeout";
                    else
                    {
                        result.responseJson=await work.ConfigureAwait(false); var body=StarfallLivingMemoryClient.Map(result.responseJson);
                        if(!string.Equals((string)body["model"],model,StringComparison.Ordinal))
                            throw new FormatException("model-identity-mismatch");
                        var choices=(List<object>)body["choices"];
                        if(choices.Count!=1)throw new FormatException("single choice required");
                        var choice=(Dictionary<string,object>)choices[0]; result.finishReason=(string)choice["finish_reason"];
                        if(result.finishReason!="stop")throw new FormatException("incomplete response");
                        result.answer=(string)((Dictionary<string,object>)choice["message"])["content"];
                        result.status=Parse(result.answer,eligible,out _)?"parsed-awaiting-live-check":"schema-rejected";
                    }
                }
                catch(OperationCanceledException){result.status="cancelled";}
                catch(Exception){result.status="provider-unavailable";}
                finally
                {
                    local.Cancel();result.milliseconds=timer.ElapsedMilliseconds;
                    if(result.milliseconds>DeadlineMilliseconds&&result.status=="parsed-awaiting-live-check")result.status="timeout";
                    if(work!=null)_=work.ContinueWith(t=>{var observed=t.Exception;},CancellationToken.None,
                        TaskContinuationOptions.OnlyOnFaulted|TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);
                }
            }
            return result;
        }
    }
}
