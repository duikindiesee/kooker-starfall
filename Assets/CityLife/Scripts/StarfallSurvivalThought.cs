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
        // the 1500 ms immediate action/reflection deadline. Warm local E4B
        // GGUF returned exact eligible final-content actions for all twelve
        // ordered cardinal pairs under 2.54 s. The older remote Nano path
        // length-truncated about half those pairs under this same bound.
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
        public static string BuildRequest(string model, int hunger, int thirst, IReadOnlyCollection<string> eligible,
            int carriedFruit=0, bool mealOutcomeVerified=false, string recentVerifiedOutcome=null,
            string verifiedDeathCause=null)
        {
            if(eligible==null||eligible.Count==0||eligible.Count>8)throw new ArgumentException("bounded eligible actions required");
            bool explorationOnly=true;
            foreach(string action in eligible)
                if(action==null||!action.StartsWith("explore ",StringComparison.Ordinal))explorationOnly=false;
            // Exact warm Nano probes: two pure exploration actions plus need
            // context ended length/empty 5/5, while the same short eligible
            // list produced complete final actions 5/5 inside this deadline.
            // There is no observed food/drink to weigh in this branch; omit
            // irrelevant physiology rather than weakening the time bound.
            // A short-term resolved action is not world knowledge: only the
            // executed route/food receipt may enter this field. It supplies the
            // missing multi-step experiment context (approach -> gather) without
            // asserting that untested fruit is edible or nutritious.
            string prior=recentVerifiedOutcome=="approach berry reached"?
                "Last verified outcome: approach berry reached observed fruit. Gathering stores one fruit for a test; food benefit unknown. ":
                recentVerifiedOutcome=="gather berry succeeded"?
                "Last verified outcome: gather berry put one observed fruit in inventory. Eating it would test an unknown meal effect. ":
                recentVerifiedOutcome=="catch fish succeeded"?
                "Last verified outcome: caught freshwater fish in shallows. ":
                recentVerifiedOutcome=="catch crab succeeded"?
                "Last verified outcome: caught protein crab on shore. ":
                recentVerifiedOutcome=="eat catch succeeded"?
                "Last verified outcome: ate fresh catch to reduce hunger. ":
                recentVerifiedOutcome=="eat fruit succeeded"?
                "Last verified outcome: ate ripe fruit to reduce hunger. ":
                recentVerifiedOutcome=="feast catch succeeded"?
                "Last verified outcome: feasted on savory roasted meal. ":
                recentVerifiedOutcome=="approach spring reached"?
                "Last verified outcome: approach spring reached observed seep. ":
                recentVerifiedOutcome=="inspect spring succeeded"?
                "Last verified outcome: inspected the observed maintained freshwater seep. ":
                recentVerifiedOutcome=="drink river succeeded"?
                "Last verified outcome: drank fresh river water. ":
                recentVerifiedOutcome=="drink water succeeded"?
                "Last verified outcome: drank carried freshwater from container. ":
                recentVerifiedOutcome=="seek water started"?
                "Last verified outcome: seeking freshwater river to quench thirst. ":
                recentVerifiedOutcome!=null&&recentVerifiedOutcome.EndsWith(" reached",StringComparison.Ordinal)?
                "Last verified outcome: "+recentVerifiedOutcome+". ":"";
            // Only the two measured, hash-validated own-death causes can enter
            // the request. Never forward arbitrary saved text as a model fact.
            string death=verifiedDeathCause=="prolonged-dehydration"?
                "Prior verified own death: Hydration remained depleted before fatal damage. ":
                verifiedDeathCause=="prolonged-starvation"?
                "Prior verified own death: Energy and fat were exhausted before fatal damage. ":
                verifiedDeathCause=="drowning"?
                "Prior verified own death: Submerged underwater without air; drowned. ":"";
            string energyLabel=hunger<2000?"severe low energy":hunger<8500?"below replenish target":"at replenish target";
            string waterLabel=thirst<2000?"severe low hydration":thirst<8500?"below replenish target":"at replenish target";
            string user=explorationOnly?death+prior+"Eligible: "+string.Join(", ",eligible)+".":
                "Energy="+hunger+"/10000 "+energyLabel+
                "; water="+thirst+"/10000 "+waterLabel+
                "; carried fruit="+carriedFruit+
                "; eaten fruit outcome="+(mealOutcomeVerified?"previously helped":"not observed")+
                ". "+death+prior+"Eligible: "+string.Join(", ",eligible)+".";
            return NpcBoundedJson.Encode(new Dictionary<string,object> {
                ["model"]=model,["stream"]=false,["temperature"]=0,["max_tokens"]=128,["reasoning_effort"]="none",
                ["messages"]=new object[] {
                    new Dictionary<string,object>{["role"]="system",["content"]="Goal: stay alive and discover resources. Inspect unknown visible things before using them. Choose exactly one listed two-word action. No extra words."},
                    new Dictionary<string,object>{["role"]="user",["content"]=user}
                }
            });
        }
        public static async Task<Result> Request(string endpoint,string model,int hunger,int thirst,
            IReadOnlyCollection<string> eligible,CancellationToken cancellation,
            int carriedFruit=0,bool mealOutcomeVerified=false,string recentVerifiedOutcome=null,
            string preparedRequestJson=null,string verifiedDeathCause=null)
        {
            string expected=BuildRequest(model,hunger,thirst,eligible,carriedFruit,mealOutcomeVerified,
                recentVerifiedOutcome,verifiedDeathCause);
            if(preparedRequestJson!=null && !string.Equals(preparedRequestJson,expected,StringComparison.Ordinal))
                throw new ArgumentException("issued-request-context-mismatch");
            var result=new Result{model=model,requestJson=preparedRequestJson??expected};
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
