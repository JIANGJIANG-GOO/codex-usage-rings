using System;using CodexUsageRings;using System.Collections.Generic;using System.Web.Script.Serialization;
public static class ResetCardTests {
 static int count;static void Check(bool ok,string name){if(!ok)throw new Exception(name);count++;}
 public static int Run(){count=0;var gate=new ResetConfirmation();
 Check(!gate.Click("a",0),"first reset click only arms");
 Check(gate.Selected=="a","first click changes selected button to confirm");
 Check(!gate.Click("a",499),"499ms cannot reset");
 Check(!gate.Click("a",500),"exactly 500ms cannot reset");
 Check(gate.Click("a",501),"strictly over 500ms confirms");
 Check(!gate.Click("a",1200),"busy prevents repeat submission");
 gate.Complete();Check(!gate.Click("a",2000),"next operation requires two clicks");
 Check(!gate.Click("b",2600)&&gate.Selected=="b","switching card cancels old confirmation");
 Check(!gate.Click("b",12600),"ten second timeout rearms instead of submitting");
 gate.Cancel();Check(gate.Selected==null,"closing clears confirmation");
 Check(!gate.Click("b",13000),"reopening needs first click again");
 gate.Cancel();Check(!gate.Click(null,14000),"missing card never submits");
 string json=ListJson("account-A","available","codexRateLimits",null);
 var list=ResetCardService.ParseList(json);Check(list.DetailsKnown&&list.Cards.Count==1&&list.AvailableCount==3,"count may exceed fetched card rows");
 Check(list.Cards[0].Available(DateTime.UtcNow),"valid available card accepted");
 Check(!ResetCardService.ParseList(ListJson("a","redeemed","codexRateLimits",null)).Cards[0].Available(DateTime.UtcNow),"redeemed disabled");
 Check(!ResetCardService.ParseList(ListJson("a","available","unknown",null)).Cards[0].Available(DateTime.UtcNow),"unsupported card disabled");
 Check(!ResetCardService.ParseList(ListJson("a","available","codexRateLimits",1)).Cards[0].Available(DateTime.UtcNow),"expired disabled");
 Check(!ResetCardService.ParseList(ListJson("a","available","codexRateLimits","bad")).Cards[0].Available(DateTime.UtcNow),"invalid expiry never unlimited");
 Check(ResetCardService.ParseList(null).Error!=null,"bad response reports error");
 var counts=ResetCardService.ParseList("{\"result\":{\"rateLimitResetCredits\":{\"availableCount\":3,\"credits\":null}}}");Check(!counts.DetailsKnown&&counts.Cards.Count==0,"count-only data never fabricates cards");
 foreach(string outcome in new[]{"reset","alreadyRedeemed","nothingToReset","noCredit","bad"}){
  var result=ResetCardService.ParseOutcome("{\"result\":{\"outcome\":\""+outcome+"\"}}");Check(result.Success==(outcome=="reset"||outcome=="alreadyRedeemed"),"outcome "+outcome);
 }
 int consumes=0;string response=json;var keys=new List<string>();
 using(var service=new ResetCardService(delegate(string method,object args){
  if(method=="account/rateLimits/read")return response;
  consumes++;var param=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(new JavaScriptSerializer().Serialize(args));
  Check((string)param["creditId"]=="card-A","explicit selected ID sent");keys.Add((string)param["idempotencyKey"]);return null;
 })){
  Check(!service.Consume(list,"other").Success&&consumes==0,"unlisted ID never submitted");
  response=ListJson("account-B","available","codexRateLimits",null);Check(!service.Consume(list,"card-A").Success&&consumes==0,"account switch prevents consume");
  response=ListJson("account-A","redeemed","codexRateLimits",null);Check(!service.Consume(list,"card-A").Success&&consumes==0,"preflight rejects spent card");
  response=json;Check(!service.Consume(list,"card-A").Success,"uncertain result is not success");
  service.Consume(list,"card-A");Check(consumes==2&&keys[0]==keys[1],"uncertain user retry reuses idempotency key");
 }
 return count;}
 internal static string ListJson(string account,string status,string type,object expiry){return new JavaScriptSerializer().Serialize(new {result=new {accountId=account,rateLimitResetCredits=new {availableCount=3,credits=new[]{new {id="card-A",title="Full reset",description="Test card only",status=status,resetType=type,grantedAt=1,expiresAt=expiry}}}}});}
}
