using System;
using System.IO;
using UnityEditor;
using UnityEngine;
namespace CityLife.Items.Editor {
 public static class BasketValidationReceipt {
  [Serializable] public class Receipt { public string status; public string[] checks; public string error; public string utc; }
  public static void Run() {
   var args=Environment.GetCommandLineArgs();
   var index=Array.IndexOf(args,"-physicalItemEvidence");
   if(index<0 || index+1>=args.Length) throw new Exception("Missing evidence directory");
   var receipt=new Receipt(); var code=1;
   try { receipt.checks=BasketPersistenceChecks.Run().ToArray(); receipt.status="PASS"; code=0; }
   catch(Exception e) { receipt.status="FAIL"; receipt.error=e.ToString(); Debug.LogException(e); }
   receipt.utc=DateTime.UtcNow.ToString("o");
   File.WriteAllText(Path.Combine(args[index+1],"summary.json"),JsonUtility.ToJson(receipt,true));
   Debug.Log("BASKET_SUITE "+receipt.status+" named checks="+(receipt.checks==null?0:receipt.checks.Length));
   EditorApplication.Exit(code);
  }
 }
}
