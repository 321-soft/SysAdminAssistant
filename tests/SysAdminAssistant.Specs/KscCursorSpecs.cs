using SysAdminAssistant.Core;
internal static class KscCursorSpecs{
 public static async Task Run(Action<string,bool> check){
  var rows=Enumerable.Range(1,1000).Select(i=>new KscEvent(i*3,"device","example",2)).ToArray();
  Task<KscEvent[]> Range(int a,int b)=>Task.FromResult(rows[a..(b+1)]);
  var first=await KscCursor.Read(800,null,Range);check("KSC initial attach skips old events",first.Cursor==2400&&first.Rows.Length==0);
  var next=await KscCursor.Read(1000,first.Cursor,Range);check("KSC catch-up reads bounded ordered page",next.Rows.Length==128&&next.Rows[0].Id==2403&&next.More);
  var tail=await KscCursor.Read(1000,next.Cursor,Range);check("KSC resume finishes without overlap",!tail.More&&tail.Rows.Length==72&&tail.Rows.First().Id==next.Cursor+3);
  var unchanged=await KscCursor.Read(1000,tail.Cursor,Range);check("KSC restart at saved cursor returns no duplicate rows",unchanged.Rows.Length==0);
  try{await KscCursor.Read(10,tail.Cursor,Range);check("KSC regressed journal rejected",false);}catch(InvalidDataException){check("KSC regressed journal rejected",true);}
  var empty=await KscCursor.Read(0,null,(_,_)=>throw new Exception("Unexpected range"));
  check("KSC first connection to empty journal establishes zero baseline",empty.Cursor==0&&empty.Rows.Length==0);
  var stillEmpty=await KscCursor.Read(0,0,(_,_)=>throw new Exception("Unexpected range"));
  check("KSC unchanged empty journal remains a valid poll",stillEmpty.Cursor==0);
  try{await KscCursor.Read(0,tail.Cursor,Range);check("KSC cleared journal cannot masquerade as successful idle poll",false);}
  catch(InvalidDataException e){check("KSC cleared journal cannot masquerade as successful idle poll",e.Message.Contains("позиция чтения сохранена"));}
  var resumed=await KscCursor.Read(1000,first.Cursor,Range);
  check("KSC temporary empty response does not prevent retry at previous cursor",resumed.Cursor==next.Cursor&&resumed.Rows.SequenceEqual(next.Rows));
  try{await KscCursor.Read(1000,-1,Range);check("KSC negative persisted cursor rejected",false);}
  catch(InvalidDataException){check("KSC negative persisted cursor rejected",true);}
  var sparse=new[]{new KscEvent(100,"device","event",2),new KscEvent(900,"device","event",2)};
  var sparseBatch=await KscCursor.Read(2,100,(a,b)=>Task.FromResult(sparse[a..(b+1)]));
  check("KSC noncontiguous IDs alone are not a false journal-loss alarm",sparseBatch.Cursor==900&&sparseBatch.Rows.Length==1);

 }
}
