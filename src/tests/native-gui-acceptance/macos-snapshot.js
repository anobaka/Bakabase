// Read-only. Loaded by probe.py only after its disposable macOS runner guard.
// Input values and URLs are intentionally absent from the evidence schema.
let stage = 'initialize';
function main() {
  const began = Date.now(), se = Application('System Events');
  stage = 'preflight';
  const output = {backend:'macos-system-events-ax', readOnly:true, enabled:se.uiElementsEnabled(),
    windows:[], truncated:false};
  if (!output.enabled) return output;
  stage = 'resolve-process';
  const processes = se.processes.whose({unixId:input.pid})();
  if (processes.length !== 1) throw new Error('ExactProductProcessUnavailable');
  const process = processes[0];
  function attribute(element, name) {
    try { return element.attributes.byName(name).value(); } catch (_) { return null; }
  }
  function text(value) { return typeof value === 'string' ? value.slice(0,300) : ''; }
  let count = 0;
  function walk(element, path, nodes, depth, inWeb) {
    if (count >= input.maxNodes || depth > input.maxDepth || Date.now() - began > 24000) {
      output.truncated = true; return;
    }
    count++;
    const role = text(attribute(element,'AXRole'));
    const password = attribute(element,'AXSubrole') === 'AXSecureTextField';
    inWeb = inWeb || role === 'AXWebArea';
    let actions = [];
    if (!password) {
      try { actions = element.actions().map(action => text(action.name())).slice(0,12); } catch (_) {}
    }
    nodes.push({path:path, role:role, name:password ? '' : text(attribute(element,'AXTitle')) || text(attribute(element,'AXDescription')),
      text:role === 'AXStaticText' ? text(attribute(element,'AXValue')) : '',
      identifier:text(attribute(element,'AXIdentifier')), enabled:attribute(element,'AXEnabled') === true,
      insideWebContent:inWeb, password:password, actions:actions});
    let children = []; try { children = element.uiElements(); } catch (_) {}
    for (let i=0; i<children.length; i++) {
      if (count >= input.maxNodes) { output.truncated = true; break; }
      walk(children[i],path.concat(i),nodes,depth+1,inWeb);
    }
  }
  stage = 'enumerate-windows';
  const windows = process.windows(), applicationVisible = process.visible();
  if (windows.length > 8) output.truncated = true;
  for (let i=0; i<Math.min(windows.length,8); i++) {
    const window = windows[i], nodes = [];
    const minimized = attribute(window,'AXMinimized') === true;
    stage = 'read-tree';
    walk(window,[i],nodes,0,false);
    output.windows.push({index:i,name:text(attribute(window,'AXTitle')),visible:applicationVisible && !minimized,nodes:nodes});
  }
  stage = 'verify-process';
  if (process.unixId() !== input.pid) throw new Error('ProductProcessChanged');
  output.elapsedMs = Date.now()-began;
  return output;
}
let result;
try { result = main(); }
catch (_) { result = {backend:'macos-system-events-ax',readOnly:true,enabled:false,windows:[],errorStage:stage}; }
JSON.stringify(result);
