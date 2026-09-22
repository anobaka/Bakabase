// A separate diagnostic, never an alternate driver or an accessibility prompt.
// The caller has verified the exact owned executable/PID/start before invocation.
let stage='load-framework', trusted=null;
const began=Date.now();
function result(code, windows=0) {
  return {readOnly:true, trusted:trusted, available:code===null,
    code:code, stage:stage, ownedWindowCount:windows, elapsedMs:Date.now()-began};
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
  if($.AXUIElementGetPid(application,actualPid)!==0 || Number(actualPid[0])!==input.pid)
    return result('DirectAXProcessMismatch');
  // This only changes the messaging timeout of our own client-side AX object.
  if($.AXUIElementSetMessagingTimeout(application,0.5)!==0)
    return result('DirectAXTimeoutUnavailable');
  stage='read-owned-application-role';
  const role=Ref();
  if($.AXUIElementCopyAttributeValue(application,$('AXRole'),role)!==0 ||
     $.CFGetTypeID(role[0])!==$.CFStringGetTypeID() || ObjC.unwrap(role[0])!=='AXApplication')
    return result('DirectAXApplicationUnavailable');
  stage='read-owned-window-count';
  const windows=Ref();
  if($.AXUIElementCopyAttributeValue(application,$('AXWindows'),windows)!==0 ||
     $.CFGetTypeID(windows[0])!==$.CFArrayGetTypeID())
    return result('DirectAXWindowsUnavailable');
  const count=Number($.CFArrayGetCount(windows[0]));
  if(!Number.isInteger(count) || count<0 || count>8) return result('DirectAXWindowBudgetExceeded');
  if(Date.now()-began>3000) return result('DirectAXReadBudgetExceeded');
  stage='complete';
  return result(count>0 ? null : 'DirectAXNoOwnedWindow',count);
}
let output;
try { output=main(); } catch (_) { output=result('DirectAXDiagnosticUnavailable'); }
JSON.stringify(output);
