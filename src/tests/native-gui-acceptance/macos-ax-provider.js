// Public direct AX only. Loaded after the Python hosted/executable/PID guard.
// CoreFoundation objects never enter the persisted snapshot. Editable AXValue
// is never requested; only AXStaticText may expose its displayed value.
function ownedAX(input, budgetMs) {
  const began=Date.now(), limit=Math.min(24000,budgetMs);
  const interactive=['AXButton','AXLink','AXMenuItem','AXCheckBox','AXRadioButton','AXTextField','AXTextArea','AXPopUpButton'];
  const editable=['AXTextField','AXTextArea','AXComboBox'];
  const allowed=['AXRole','AXSubrole','AXTitle','AXDescription','AXIdentifier','AXEnabled',
    'AXHidden','AXMinimized','AXChildren','AXWindows','AXValue','AXPosition','AXSize','AXParent'];
  let stage='initialize', operation='initialize', reads=0, pidMismatch=null;
  function fail(code, attribute=null, axError=null, count=null) {
    throw {safe:true,code:code,stage:stage,operation:operation,attribute:attribute,axError:axError,
      countKind:count?count.kind:null,countValue:count?count.value:null};
  }
  function check() {
    if(Date.now()-began>=limit) fail('DirectAXTreeDeadline');
    if(++reads>16000) fail('DirectAXReadCountExceeded');
  }
  function same(a,b) {return !!$.CFEqual(cfValue(a).object,cfValue(b).object);}
  function typed(value,type) {
    const converted=cfValue(value);
    if(converted.typeId!==Number(type())) fail('DirectAXValueTypeMismatch');
    return converted.object;
  }
  function string(value) {
    if(value===null) return '';
    const text=ObjC.unwrap(typed(value,$.CFStringGetTypeID));
    if(typeof text!=='string') fail('DirectAXValueTypeMismatch');
    return text.slice(0,300);
  }
  function boolean(value) {
    if(value===null) return null;
    return !!$.CFBooleanGetValue(typed(value,$.CFBooleanGetTypeID));
  }
  function array(value,max) {
    operation='array-type';
    const object=typed(value,$.CFArrayGetTypeID);
    operation='array-count';
    const count=Number($.CFArrayGetCount(object));
    if(!Number.isInteger(count)||count<0||count>max) fail('DirectAXCollectionBudgetExceeded');
    const values=[];
    operation='array-item';
    for(let i=0;i<count;i++) values.push($.CFArrayGetValueAtIndex(object,i));
    return values;
  }
  function element(value) {
    operation='element-type';
    // CFArray items are const void* Refs, not AXUIElementRef-typed Refs. The
    // validated CF object preserves identity and lets JXA bridge typed calls.
    const ref=typed(value,$.AXUIElementGetTypeID);
    check();
    operation='element-timeout';
    const error=Number($.AXUIElementSetMessagingTimeout(ref,0.5));
    if(error!==0) fail('DirectAXCallFailed',null,error);
    return ref;
  }
  function read(ref,name,optional=false) {
    if(!allowed.includes(name)) fail('DirectAXAttributeRejected');
    check();
    operation=({AXRole:'read-role',AXSubrole:'read-subrole',AXTitle:'read-title',AXDescription:'read-description',
      AXIdentifier:'read-identifier',AXEnabled:'read-enabled',AXHidden:'read-hidden',AXMinimized:'read-minimized',
      AXChildren:'read-children',AXWindows:'read-windows',AXValue:'read-static-text',AXPosition:'read-position',
      AXSize:'read-size',AXParent:'read-parent'})[name];
    const value=Ref(),error=Number($.AXUIElementCopyAttributeValue(ref,$(name),value));
    // AX explicitly reports absence separately from inability to read. Unknown
    // failures never turn into an empty subtree or a successful absence check.
    if(optional&&(error===-25205||(error===-25212&&name!=='AXChildren'))) return null;
    if(error!==0) fail('DirectAXCallFailed',name,error);
    return value[0];
  }
  function children(ref,role) {
    // Only the known static-text leaf may omit AXChildren. A container, an
    // editable control or an unknown role must return an array or independently
    // confirm that a NoValue attribute has exactly zero elements.
    let value;
    try {value=read(ref,'AXChildren',role==='AXStaticText');}
    catch(error) {
      if(!error||!error.safe||error.attribute!=='AXChildren'||error.axError!==-25212) throw error;
      verifyPid(ref);check();operation='read-children-count';
      const count=Ref(),countError=Number($.AXUIElementGetAttributeValueCount(ref,$('AXChildren'),count));
      if(countError!==0) fail('DirectAXCallFailed','AXChildren',countError);
      const normalized=cfIndex(count[0],input.maxNodes);
      if(normalized.value!==0)
        fail('DirectAXChildrenCountMismatch','AXChildren',-25212,normalized);
      verifyPid(ref);
      return {values:[],evidence:'no-value-count-zero',countKind:normalized.kind};
    }
    return {values:value===null?[]:array(value,input.maxNodes),
      evidence:value===null?'static-text-unsupported':'explicit-array'};
  }
  function actions(ref) {
    check();
    operation='read-actions';
    const value=Ref(),error=Number($.AXUIElementCopyActionNames(ref,value));
    if(error!==0) fail('DirectAXCallFailed',null,error);
    return array(value[0],12).map(string);
  }
  function metadata(ref,role) {
    const password=['AXTextField','AXTextArea'].includes(role)&&string(read(ref,'AXSubrole',true))==='AXSecureTextField';
    return {password:password,name:password?'':string(read(ref,'AXTitle',true))||string(read(ref,'AXDescription',true)),
      identifier:password?'':string(read(ref,'AXIdentifier',true)),enabled:boolean(read(ref,'AXEnabled',true))===true};
  }
  function visible(application,window) {
    return boolean(read(application,'AXHidden'))===false&&boolean(read(window,'AXMinimized'))===false;
  }
  function pair(value,type) {
    operation='geometry-decode';
    try {return cfPair(value,type);} catch(_) {fail('DirectAXGeometryUnavailable');}
  }
  function bounds(ref) {
    const point=pair(read(ref,'AXPosition'),1),size=pair(read(ref,'AXSize'),2);
    return {x:point[0],y:point[1],width:size[0],height:size[1]};
  }
  function exposed(application,window,windowBounds,ref) {
    if(!visible(application,window)) return {visible:false,evidence:'window-hidden'};
    verifyPid(ref);
    const rect=bounds(ref);
    if(rect.width<=0||rect.height<=0) return {visible:false,evidence:'zero-size'};
    const left=Math.max(rect.x,windowBounds.x),top=Math.max(rect.y,windowBounds.y);
    const right=Math.min(rect.x+rect.width,windowBounds.x+windowBounds.width);
    const bottom=Math.min(rect.y+rect.height,windowBounds.y+windowBounds.height);
    if(right<=left||bottom<=top) return {visible:false,evidence:'outside-window'};
    check();
    operation='hit-test';
    const hit=Ref(),error=Number($.AXUIElementCopyElementAtPosition(application,(left+right)/2,(top+bottom)/2,hit));
    if(error===-25212) return {visible:false,evidence:'no-hit'};
    if(error!==0) fail('DirectAXCallFailed',null,error);
    let current=element(hit[0]),matched=false;
    const ancestors=[];
    for(let depth=0;depth<=input.maxDepth;depth++) {
      check();verifyPid(current); // Never inspect a foreign process returned by a hit test.
      if(ancestors.some(parent=>same(parent,current))) fail('DirectAXTreeCycle');
      ancestors.push(current);
      matched=matched||same(current,ref);
      if(same(current,window)) return {visible:matched,evidence:matched?'owned-hit-test':'other-hit'};
      if(same(current,application)) return {visible:false,evidence:'other-window'};
      const parent=read(current,'AXParent',true);
      if(parent===null) return {visible:false,evidence:'unverified-hit'};
      current=element(parent);
    }
    fail('DirectAXTreeBudgetExceeded');
  }
  function validPid(value) {return Number.isInteger(value)&&value>0&&value<2147483648;}
  function mismatchStructure(ref,actual,context) {
    // Diagnostic only: never authorizes foreign metadata or a workflow action.
    // The only foreign attributes read are two nonrecursive pointer relations.
    const detail={expectedPid:input.pid,actualPid:validPid(actual)?actual:null,
      observedEpochMs:Date.now(),origin:context?'owned-child-edge':'other',
      path:context?context.path:null,windowIndex:context?context.path[0]:null,
      parentPath:context?context.path.slice(0,-1):null,childIndex:context?context.path[context.path.length-1]:null,
      parentChildCount:context?context.childCount:null,edgeStillMatches:null,windowStillMatches:null,
      parentMatches:null,windowMatches:null,parentAXError:null,windowAXError:null,
      actualPidStable:null,status:'not-attempted'};
    if(!context||!validPid(actual)) return detail;
    const priorStage=stage,priorOperation=operation;
    try {
      const parent=context.ancestors[context.ancestors.length-1];
      // Revalidate the owned side before reading the foreign pointers. Neither
      // an arbitrary hit-test result nor an unrelated application enters here.
      verifyPid(context.application);verifyPid(context.window);verifyPid(parent);
      const windows=array(read(context.application,'AXWindows'),8);
      detail.windowStillMatches=!!windows[context.path[0]]&&same(windows[context.path[0]],context.window);
      const siblings=array(read(parent,'AXChildren'),input.maxNodes);
      const sibling=siblings[detail.childIndex];
      detail.edgeStillMatches=!!sibling&&same(sibling,ref);
      if(!detail.windowStillMatches||!detail.edgeStillMatches) {detail.status='owned-edge-changed';return detail;}
      const before=Ref();check();
      if(Number($.AXUIElementGetPid(ref,before))!==0||Number(before[0])!==actual) {detail.status='pid-changed';return detail;}
      for(const [attribute,target,matchKey,errorKey] of [
        ['AXParent',parent,'parentMatches','parentAXError'],['AXWindow',context.window,'windowMatches','windowAXError']]) {
        check();operation='diagnostic-structure';
        const value=Ref(),error=Number($.AXUIElementCopyAttributeValue(ref,$(attribute),value));
        detail[errorKey]=error;
        if(error===0) detail[matchKey]=same(typed(value[0],$.AXUIElementGetTypeID),target);
      }
      const after=Ref();check();
      detail.actualPidStable=Number($.AXUIElementGetPid(ref,after))===0&&Number(after[0])===actual;
      detail.status=detail.actualPidStable?'observed':'pid-changed';
    } catch(error) {
      detail.status=error&&error.safe&&error.code==='DirectAXTreeDeadline'?'budget-exhausted':'diagnostic-unavailable';
    } finally {stage=priorStage;operation=priorOperation;}
    return detail;
  }
  function verifyPid(application,context=null) {
    check();
    operation='read-pid';
    const pid=Ref(),error=Number($.AXUIElementGetPid(application,pid));
    if(error!==0||Number(pid[0])!==input.pid) {
      // Reserve the record before any diagnostic revalidation can fail again.
      if(pidMismatch===null) {
        pidMismatch={expectedPid:input.pid,actualPid:null,origin:'other',status:'pid-read-failed'};
        if(error===0) pidMismatch=mismatchStructure(application,Number(pid[0]),context);
      }
      fail('DirectAXProcessMismatch',null,error);
    }
  }
  function open() {
    stage='preflight';
    if(!Number.isInteger(input.pid)||input.pid<=0||input.maxNodes!==1000||input.maxDepth!==40||
       !Number.isFinite(limit)||limit<=0) fail('InvalidDirectAXInput');
    ObjC.import('ApplicationServices');
    ObjC.bindFunction('calloc',['double *',['unsigned long','unsigned long']]);
    ObjC.bindFunction('free',['void',['void *']]);
    if(!$.AXIsProcessTrusted()) fail('DirectAXNotTrusted'); // Never request permission.
    stage='resolve-process';
    const application=element($.AXUIElementCreateApplication(input.pid));
    verifyPid(application);
    if(string(read(application,'AXRole'))!=='AXApplication') fail('DirectAXApplicationRoleMismatch');
    return application;
  }
  function inspect() {
    const snapshot={backend:'macos-direct-ax',readOnly:true,enabled:false,truncated:false,windows:[]};
    const elements=new Map(), windowRefs=[];
    let application=null,count=0;
    try {
      application=open();snapshot.enabled=true;
      stage='enumerate-windows';
      const windows=array(read(application,'AXWindows'),8);
      for(let i=0;i<windows.length;i++) {
        const window=element(windows[i]),nodes=[];
        windowRefs.push(window);
        verifyPid(window);
        if(string(read(window,'AXRole'))!=='AXWindow') fail('DirectAXWindowRoleMismatch');
        const windowVisible=visible(application,window);
        const windowBounds=bounds(window);
        if(windowBounds.width<=0||windowBounds.height<=0) fail('DirectAXGeometryUnavailable');
        const record={index:i,name:string(read(window,'AXTitle',true)),visible:windowVisible,nodes:nodes};
        snapshot.windows.push(record);
        function walk(value,path,depth,inWeb,ancestors,editableAncestor,parentChildCount=null) {
          check();
          if(count>=input.maxNodes||depth>input.maxDepth) fail('DirectAXTreeBudgetExceeded');
          const ref=element(value);
          verifyPid(ref,ancestors.length?{application:application,window:window,path:path,
            ancestors:ancestors,childCount:parentChildCount}:null); // Before foreign role/text/metadata.
          if(ancestors.some(parent=>same(parent,ref))) fail('DirectAXTreeCycle');
          count++;
          const role=string(read(ref,'AXRole'));
          if(!role) fail('DirectAXValueTypeMismatch');
          const control=interactive.includes(role)&&!editableAncestor,info=control?metadata(ref,role):{};
          inWeb=inWeb||role==='AXWebArea';
          const node={path:path,role:role,name:editableAncestor?'':control?info.name:role==='AXWebArea'?
            string(read(ref,'AXTitle',true))||string(read(ref,'AXDescription',true)):'',
            text:!editableAncestor&&role==='AXStaticText'?string(read(ref,'AXValue',true)):'',identifier:info.identifier||'',
            enabled:control&&info.enabled===true,visible:false,visibilityEvidence:'not-observed',insideWebContent:inWeb,
            editableAncestor:editableAncestor,password:info.password===true,actions:control&&!info.password?actions(ref):[]};
          // A visible window is not proof that an individual control/text is
          // exposed. Read-only, application-scoped hit testing also excludes
          // clipped, offscreen and occluded elements; it never clicks a point.
          if(inWeb&&!editableAncestor&&(control||role==='AXStaticText')&&!info.password) {
            const exposure=exposed(application,window,windowBounds,ref);
            node.visible=exposure.visible;node.visibilityEvidence=exposure.evidence;
          }
          nodes.push(node);elements.set(path.join('/'),ref);
          const childResult=children(ref,role),nested=childResult.values;
          node.childrenEvidence=childResult.evidence;node.childCount=nested.length;
          node.childrenCountKind=childResult.countKind||null;
          for(let child=0;child<nested.length;child++) walk(nested[child],path.concat(child),depth+1,inWeb,ancestors.concat([ref]),
            editableAncestor||editable.includes(role),nested.length);
        }
        stage='read-tree';
        walk(window,[i],0,false,[],false);
      }
      stage='verify-process';verifyPid(application);
    } catch(error) {
      snapshot.truncated=true;
      snapshot.errorStage=error&&error.safe?error.stage:stage;
      snapshot.diagnostic=error&&error.safe?{code:error.code,operation:error.operation,attribute:error.attribute,axError:error.axError,
        countKind:error.countKind,countValue:error.countValue}:
        {code:'DirectAXTreeUnavailable',operation:operation,attribute:null,axError:null};
    }
    snapshot.pidMismatch=pidMismatch;
    snapshot.elapsedMs=Date.now()-began;
    return {snapshot:snapshot,application:application,windows:windowRefs,elements:elements};
  }
  function resolve(application,path) {
    const windows=array(read(application,'AXWindows'),8);
    if(!windows[path[0]]) fail('DirectAXControlChanged');
    let ref=element(windows[path[0]]),inWeb=false;
    verifyPid(ref);
    if(!visible(application,ref)) fail('DirectAXControlInvisible');
    const window=ref;
    for(let i=1;i<path.length;i++) {
      const nested=children(ref,string(read(ref,'AXRole'))).values;
      if(!nested[path[i]]) fail('DirectAXControlChanged');
      ref=element(nested[path[i]]);
      verifyPid(ref);
      inWeb=inWeb||string(read(ref,'AXRole'))==='AXWebArea';
    }
    if(!inWeb) fail('DirectAXControlOutsideWeb');
    return {ref:ref,window:window};
  }
  function press() {
    stage='validate-control';
    const expected=input.selector,path=expected&&expected.path;
    if(input.operation!=='press'||!Array.isArray(path)||path.length<2||path.length>42||
       path.some(i=>!Number.isInteger(i)||i<0||i>=1000)||
       !['AXButton','AXLink','AXMenuItem','AXCheckBox','AXRadioButton','AXPopUpButton'].includes(expected.role))
      fail('InvalidDirectAXInput');
    // Re-read the full current tree in this same provider before a mutation.
    // Partial trees cannot establish uniqueness, even when the target is found.
    const view=inspect(),snapshot=view.snapshot;
    if(!snapshot.enabled||snapshot.truncated) {
      if(snapshot.diagnostic) throw {safe:true,stage:snapshot.errorStage||'read-tree',
        code:snapshot.diagnostic.code,operation:snapshot.diagnostic.operation,
        attribute:snapshot.diagnostic.attribute,axError:snapshot.diagnostic.axError,
        countKind:snapshot.diagnostic.countKind,countValue:snapshot.diagnostic.countValue};
      fail('DirectAXActionTreeIncomplete');
    }
    const candidates=snapshot.windows.filter(w=>w.visible).flatMap(w=>w.nodes).filter(n=>n.insideWebContent&&
      n.visible&&n.enabled&&!n.password&&n.role===expected.role&&n.name===expected.name);
    if(candidates.length!==1) fail('DirectAXControlAmbiguous');
    const found=candidates[0];
    if(found.path.join('/')!==path.join('/')||found.identifier!==expected.identifier||!found.actions.includes('AXPress'))
      fail('DirectAXControlChanged');
    stage='resolve-control';
    const current=resolve(view.application,path),held=view.elements.get(path.join('/'));
    if(!same(current.ref,held)||!same(current.window,view.windows[path[0]])) fail('DirectAXControlChanged');
    stage='validate-control';
    const role=string(read(current.ref,'AXRole')),info=metadata(current.ref,role);
    if(role!==expected.role||info.password||!info.enabled||info.name!==expected.name||info.identifier!==expected.identifier||
       !visible(view.application,current.window)||!actions(current.ref).includes('AXPress')) fail('DirectAXControlChanged');
    if(!exposed(view.application,current.window,bounds(current.window),current.ref).visible) fail('DirectAXControlInvisible');
    verifyPid(view.application);check();stage='perform-action';
    operation='press';
    const error=Number($.AXUIElementPerformAction(current.ref,$('AXPress')));
    if(error!==0) fail('DirectAXCallFailed',null,error);
    return {performed:true,operation:'press'};
  }
  return {inspect:inspect,press:press};
}
