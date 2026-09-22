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
  const metadataKeys = ['AXTitle','AXDescription','AXIdentifier','AXEnabled','AXSubrole'];
  function metadata(element) {
    try {
      // Filter the native attribute collection BEFORE requesting values. Never
      // read all values/properties: editable AXValue may contain credentials.
      const selected = element.attributes.whose({_or:metadataKeys.map(name => ({name:name}))});
      const names = selected.name(), values = selected.value();
      if (!Array.isArray(names) || !Array.isArray(values) || names.length !== values.length ||
          names.some(name => !metadataKeys.includes(name)) || new Set(names).size !== names.length) throw Error();
      const result = {};
      names.forEach((name,index) => { result[name] = values[index]; });
      return result;
    } catch (_) { output.truncated = true; return {}; }
  }
  let count = 0;
  function walk(element, path, nodes, depth, inWeb, observedRole) {
    if (count >= input.maxNodes || depth > input.maxDepth || Date.now() - began > (input.readBudgetMs || 24000)) {
      output.truncated = true; return;
    }
    count++;
    const role = typeof observedRole === 'string' ? observedRole : text(attribute(element,'AXRole'));
    const interactive = ['AXButton','AXLink','AXMenuItem','AXCheckBox','AXRadioButton','AXTextField','AXTextArea','AXPopUpButton'].includes(role);
    const info = interactive ? metadata(element) : {};
    const password = ['AXTextField','AXTextArea'].includes(role) && info.AXSubrole === 'AXSecureTextField';
    inWeb = inWeb || role === 'AXWebArea';
    let actions = [];
    if (interactive && !password) {
      try { actions = element.actions.name().map(text).slice(0,12); } catch (_) { output.truncated = true; }
    }
    // Generic layout nodes need no costly per-property Apple Events. Read only
    // controls, document titles and static text; never batch-read input values.
    nodes.push({path:path, role:role, name:password ? '' : interactive ? text(info.AXTitle) || text(info.AXDescription) :
        role==='AXWebArea' ? text(attribute(element,'AXTitle')) || text(attribute(element,'AXDescription')) : '',
      text:role === 'AXStaticText' ? text(attribute(element,'AXValue')) : '',
      identifier:interactive ? text(info.AXIdentifier) : '', enabled:interactive && info.AXEnabled === true,
      insideWebContent:inWeb, password:password, actions:actions});
    let children = []; try { children = element.uiElements(); } catch (_) { output.truncated = true; }
    let childRoles = [];
    if (children.length) {
      try {
        childRoles = element.uiElements.role();
        if (!Array.isArray(childRoles) || childRoles.length !== children.length || childRoles.some(role => typeof role !== 'string')) throw Error();
      } catch (_) { output.truncated = true; childRoles = []; }
    }
    for (let i=0; i<children.length; i++) {
      if (count >= input.maxNodes) { output.truncated = true; break; }
      walk(children[i],path.concat(i),nodes,depth+1,inWeb,childRoles[i]);
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
