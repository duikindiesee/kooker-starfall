using System;using System.IO;using UnityEditor;using UnityEngine;
namespace CityLife.Fire.Editor {
 public static class TinderValidationReceipt {
  [Serializable] public class Receipt {public string status;public string error;public TinderValidation.ValidationReport validation;public TinderValidation.SelfTestReport selfTests;}
  public static void Run(){var args=Environment.GetCommandLineArgs();string output=args[Array.IndexOf(args,"-physicalItemEvidence")+1];var r=new Receipt();int exit=1;
   try{r.selfTests=TinderValidation.RunAggregationSelfTests();r.validation=TinderValidation.RunValidation();bool self=r.selfTests!=null&&r.selfTests.status=="PASSED"&&r.selfTests.totalSelfTests>0&&r.selfTests.passedSelfTests==r.selfTests.totalSelfTests;bool valid=TinderValidation.EvaluateAggregationDecision(r.validation,out int code);r.status=self&&valid&&code==0?"PASS":"FAIL";exit=r.status=="PASS"?0:1;}
   catch(Exception e){r.status="FAIL";r.error=e.ToString();Debug.LogException(e);}
   File.WriteAllText(Path.Combine(output,"summary.json"),JsonUtility.ToJson(r,true));EditorApplication.Exit(exit);
  }
 }
}
