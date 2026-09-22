// Exact owned PID + observed path + semantic revalidation. No coordinate clicks.
let stage='initialize';
function main() {
  const se=Application('System Events');
  stage='preflight';
  if(!se.uiElementsEnabled()) throw Error();
  stage='resolve-process';
  const processes=se.processes.whose({unixId:input.pid})();
  if(processes.length!==1) throw Error();
  const process=processes[0], path=input.selector.path;
  const windows=process.windows();
  if(path.length<2 || path.length>42 || path.some(i=>!Number.isInteger(i)||i<0) || !windows[path[0]]) throw Error();
  if(!process.visible()) throw Error();
  function attr(e,key) { try{return e.attributes.byName(key).value()}catch(_){return null} }
  function text(v) {return typeof v==='string'?v.slice(0,300):''}
  let element=windows[path[0]], inWeb=false;
  if(attr(element,'AXMinimized')===true) throw Error();
  stage='resolve-control';
  for(let i=1;i<path.length;i++) {
    const children=element.uiElements();
    if(!children[path[i]]) throw Error();
    element=children[path[i]];
    inWeb=inWeb || attr(element,'AXRole')==='AXWebArea';
  }
  const role=text(attr(element,'AXRole'));
  const name=text(attr(element,'AXTitle')) || text(attr(element,'AXDescription'));
  stage='validate-control';
  if(!inWeb || role!==input.selector.role || name!==input.selector.name ||
     text(attr(element,'AXIdentifier'))!==input.selector.identifier ||
     attr(element,'AXEnabled')!==true || attr(element,'AXSubrole')==='AXSecureTextField') throw Error();
  if(!['AXButton','AXLink','AXMenuItem','AXCheckBox','AXRadioButton','AXTextField','AXTextArea','AXPopUpButton'].includes(role)) throw Error();
  if(process.unixId()!==input.pid) throw Error();
  stage='perform-action';
  if(input.operation==='press') {
    if(!element.actions().some(action=>action.name()==='AXPress')) throw Error();
    element.actions.byName('AXPress').perform();
  } else if(input.operation==='set' && ['AXTextField','AXTextArea'].includes(role)) {
    if(element.attributes.byName('AXValue').settable()!==true) throw Error();
    element.value=input.value;
  } else throw Error();
  return {performed:true,operation:input.operation};
}
let result;
try {result=main()} catch(_) {result={performed:false,errorStage:stage}}
JSON.stringify(result);
