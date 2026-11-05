using System;
using System.Collections;
using System.Collections.Generic;
using System.Web.Script.Serialization;
namespace CodexUsageRings {
 public sealed class ResetConfirmation {
  public string Selected;public bool Busy;long armedAt;
  public bool Click(string id,long milliseconds){
   if(Busy||String.IsNullOrEmpty(id))return false;
   if(Selected!=id||milliseconds<armedAt||milliseconds-armedAt>=10000){Selected=id;armedAt=milliseconds;return false;}
   if(milliseconds-armedAt<=500)return false;
   Busy=true;Selected=null;return true;
  }
  public void Expire(long milliseconds){if(Selected!=null&&(milliseconds<armedAt||milliseconds-armedAt>=10000))Cancel();}
  public void Complete(){Busy=false;Cancel();}
  public void Cancel(){Selected=null;}
 }
 public sealed class ResetCard {
  public string Id,Title,Description,Status,ResetType;
  public DateTime? ExpiresAtUtc,GrantedAtUtc;
  public bool Available(DateTime now){return !String.IsNullOrWhiteSpace(Id)&&Status=="available"&&ResetType=="codexRateLimits"&&(!ExpiresAtUtc.HasValue||ExpiresAtUtc.Value>now);}
 }
 public sealed class ResetCardList {
  public string AccountId,Error;public long AvailableCount;public bool DetailsKnown;
  public List<ResetCard> Cards=new List<ResetCard>();
 }
 public sealed class ResetResult {
  public string Outcome,Message;
  public bool Success{get{return Outcome=="reset"||Outcome=="alreadyRedeemed";}}
 }
 public interface IResetCardService : IDisposable {
  ResetCardList Read();ResetResult Consume(ResetCardList shown,string creditId);
 }
 public sealed class ResetCardService : IResetCardService {
  readonly UsageProvider provider;readonly Func<string,object,string> rpc;
  readonly object gate=new object();readonly Dictionary<string,string> attempts=new Dictionary<string,string>();
  public ResetCardService(){provider=new UsageProvider();rpc=provider.AccountRpc;}
  internal ResetCardService(Func<string,object,string> transport){rpc=transport;}
  public ResetCardList Read(){lock(gate)return ParseList(rpc("account/rateLimits/read",new {excludeResetCreditDetails=false,supportsLunaReserve=false}));}
  public ResetResult Consume(ResetCardList shown,string creditId){lock(gate){
   if(shown==null||String.IsNullOrWhiteSpace(shown.AccountId)||String.IsNullOrWhiteSpace(creditId))return Failure("无法确认账户或选中卡片，请刷新列表。");
   ResetCard displayed=shown.Cards.Find(c=>c.Id==creditId);
   if(displayed==null||!displayed.Available(DateTime.UtcNow))return Failure("所选重置卡不可用，请刷新列表。");
   ResetCardList current=Read();
   if(current.Error!=null)return Failure("操作前校验失败，未发送重置请求。请刷新后重试。");
   if(current.AccountId!=shown.AccountId)return Failure("账户已切换，请关闭面板后重新打开。");
   ResetCard actual=current.Cards.Find(c=>c.Id==creditId);
   if(actual==null||!actual.Available(DateTime.UtcNow))return Failure("所选卡片已不可用，未发送重置请求。");
   string attemptKey=current.AccountId+":"+creditId,key;
   if(!attempts.TryGetValue(attemptKey,out key)){key=Guid.NewGuid().ToString();attempts[attemptKey]=key;}
   // No automatic retries. A user retry for the same card reuses its key even
   // when the server's response was lost, including after the panel is closed.
   return ParseOutcome(rpc("account/rateLimitResetCredit/consume",new {creditId=creditId,idempotencyKey=key}));
  }}
  public void Dispose(){if(provider!=null)provider.Cancel();}
  static Dictionary<string,object> Obj(object value){return value as Dictionary<string,object>;}
  static object Get(Dictionary<string,object> value,string key){object found;return value!=null&&value.TryGetValue(key,out found)?found:null;}
  static string Str(Dictionary<string,object> value,string key){return Get(value,key) as string;}
  static Dictionary<string,object> Result(string json){try{var s=new JavaScriptSerializer{MaxJsonLength=1048576,RecursionLimit=64};var envelope=Obj(s.DeserializeObject(json??""));return Get(envelope,"error")==null?Obj(Get(envelope,"result")):null;}catch(ArgumentException){return null;}catch(InvalidOperationException){return null;}}
  static DateTime? Time(object value){if(!(value is int||value is long||value is double||value is decimal))return null;try{return new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddSeconds(Convert.ToDouble(value));}catch{return null;}}
  public static ResetCardList ParseList(string json){
   var list=new ResetCardList();var result=Result(json);
   if(result==null){list.Error="无法读取重置卡，请刷新重试。";return list;}
   list.AccountId=Str(result,"accountId");var summary=Obj(Get(result,"rateLimitResetCredits"));
   if(summary==null){list.Error="账户未提供重置卡信息。";return list;}
   object count=Get(summary,"availableCount");
   if(!(count is int||count is long)||Convert.ToInt64(count)<0){list.Error="重置卡数量无效。";return list;}
   list.AvailableCount=Convert.ToInt64(count);
   var rows=Get(summary,"credits") as object[];list.DetailsKnown=rows!=null;
   if(rows==null)return list;
   var ids=new HashSet<string>();
   foreach(object row in rows){var data=Obj(row);string id=Str(data,"id");if(String.IsNullOrWhiteSpace(id)||!ids.Add(id))continue;
    var card=new ResetCard{Id=id,Title=Str(data,"title")??"额度重置卡",Description=Str(data,"description")??"",Status=Str(data,"status"),ResetType=Str(data,"resetType"),ExpiresAtUtc=Time(Get(data,"expiresAt")),GrantedAtUtc=Time(Get(data,"grantedAt"))};
    // Malformed expiry must never be interpreted as an unlimited card.
    if(Get(data,"expiresAt")!=null&&!card.ExpiresAtUtc.HasValue)card.Status="unknown";
    list.Cards.Add(card);
   }return list;
  }
  static ResetResult Failure(string message){return new ResetResult{Outcome="error",Message=message};}
  public static ResetResult ParseOutcome(string json){string outcome=Str(Result(json),"outcome");
   switch(outcome){
    case "reset":return new ResetResult{Outcome=outcome,Message="重置成功，正在刷新额度。"};
    case "alreadyRedeemed":return new ResetResult{Outcome=outcome,Message="该次重置已完成，正在刷新额度。"};
    case "nothingToReset":return new ResetResult{Outcome=outcome,Message="当前没有可重置的额度周期。"};
    case "noCredit":return new ResetResult{Outcome=outcome,Message="该重置卡已不可用，请查看最新列表。"};
    default:return Failure("未确认重置结果，请先查看刷新后的额度；再次操作同一卡片会沿用本次请求标识。");
   }
  }
 }
}
