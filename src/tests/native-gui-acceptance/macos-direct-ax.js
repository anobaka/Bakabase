// A separate diagnostic, never an alternate driver or an accessibility prompt.
// The caller has verified the exact owned executable/PID/start before invocation.
let stage='load-framework', trusted=null;
const diagnostics={windowCountObserved:false};
const began=Date.now();
function result(code, windows=0) {
  return {readOnly:true, trusted:trusted, available:code===null,
    code:code, stage:stage, ownedWindowCount:windows, diagnostics:diagnostics, elapsedMs:Date.now()-began};
}
function main() {
  ObjC.import('ApplicationServices');
  stage='check-existing-trust';
  trusted=!!$.AXIsProcessTrusted(); // No WithOptions, prompt or TCC modification.
  if(!trusted) return result('DirectAXNotTrusted');
  if(!Number.isInteger(input.pid) || input.pid<=0) return result('InvalidProductPid');
  stage='create-owned-application';
  const application=$.AXUIElementCreateApplication(input.pid);
  const actualPid=Ref();
  diagnostics.pidReadError=Number($.AXUIElementGetPid(application,actualPid));
  if(diagnostics.pidReadError!==0 || Number(actualPid[0])!==input.pid)
    return result('DirectAXProcessMismatch');
  // This only changes the messaging timeout of our own client-side AX object.
  diagnostics.messagingTimeoutError=Number($.AXUIElementSetMessagingTimeout(application,0.5));
  if(diagnostics.messagingTimeoutError!==0)
    return result('DirectAXTimeoutUnavailable');
  stage='read-owned-application-role';
  const role=Ref();
  diagnostics.roleReadError=Number($.AXUIElementCopyAttributeValue(application,$('AXRole'),role));
  if(diagnostics.roleReadError!==0) return result('DirectAXRoleReadFailed');
  const roleValue=cfValue(role[0]);
  diagnostics.roleValueWasRef=roleValue.wasRef;
  diagnostics.roleRawTypeId=roleValue.rawTypeId;
  diagnostics.roleTypeId=roleValue.typeId;
  diagnostics.expectedRoleTypeId=Number($.CFStringGetTypeID());
  if(diagnostics.roleTypeId!==diagnostics.expectedRoleTypeId) return result('DirectAXRoleTypeMismatch');
  diagnostics.roleMatches=ObjC.unwrap(roleValue.object)==='AXApplication';
  if(!diagnostics.roleMatches) return result('DirectAXRoleMismatch');
  stage='read-owned-window-count';
  const windows=Ref();
  diagnostics.windowsReadError=Number($.AXUIElementCopyAttributeValue(application,$('AXWindows'),windows));
  if(diagnostics.windowsReadError!==0) return result('DirectAXWindowsReadFailed');
  const windowValue=cfValue(windows[0]);
  diagnostics.windowsValueWasRef=windowValue.wasRef;
  diagnostics.windowsRawTypeId=windowValue.rawTypeId;
  diagnostics.windowsTypeId=windowValue.typeId;
  diagnostics.expectedWindowsTypeId=Number($.CFArrayGetTypeID());
  if(diagnostics.windowsTypeId!==diagnostics.expectedWindowsTypeId) return result('DirectAXWindowsTypeMismatch');
  const count=Number($.CFArrayGetCount(windowValue.object));
  if(!Number.isInteger(count) || count<0 || count>8) return result('DirectAXWindowBudgetExceeded');
  diagnostics.windowCountObserved=true;
  if(Date.now()-began>3000) return result('DirectAXReadBudgetExceeded');
  stage='complete';
  return result(count>0 ? null : 'DirectAXNoOwnedWindow',count);
}
let output;
try { output=main(); } catch (_) { output=result('DirectAXDiagnosticUnavailable'); }
JSON.stringify(output);
