// Public direct AX only. Loaded after the Python hosted/executable/PID guard.
// CoreFoundation objects never enter the persisted snapshot. Editable AXValue
// is never requested; only AXStaticText may expose its displayed value.
function ownedAX(input, budgetMs) {
  const began=Date.now(), limit=Math.min(24000,budgetMs);
  const interactive=['AXButton','AXLink','AXMenuItem','AXCheckBox','AXRadioButton','AXTextField','AXTextArea','AXPopUpButton'];
  const editable=['AXTextField','AXTextArea','AXComboBox'];
  const semantic=['AXGroup','AXHeading','AXScrollArea','AXStaticText'];
  const allowed=['AXRole','AXSubrole','AXTitle','AXDescription','AXIdentifier','AXEnabled',
    'AXHidden','AXMinimized','AXChildren','AXWindows','AXValue','AXPosition','AXSize','AXParent'];
  let stage='initialize', operation='initialize', reads=0, pidMismatch=null, embeddedState=null;
  let rootChecks=0, maximumEmbeddedDepth=0;
  const embeddedNodes=[];
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
    const staticText=role==='AXStaticText'?string(read(ref,'AXValue',true)):null;
    return {password:password,staticText:staticText,name:password?'':role==='AXStaticText'?staticText:
      string(read(ref,'AXTitle',true))||string(read(ref,'AXDescription',true)),
      identifier:password?'':string(read(ref,'AXIdentifier',true)),enabled:boolean(read(ref,'AXEnabled',true))===true};
  }
  function settable(ref) {
    check();operation='read-value-settable';
    const value=Ref(),error=Number($.AXUIElementIsAttributeSettable(ref,$('AXValue'),value));
    if(error===-25205) return false;
    if(error!==0) fail('DirectAXCallFailed','AXValue',error);
    if(![true,false,0,1].includes(value[0])) fail('DirectAXValueTypeMismatch','AXValue');
    return value[0]===true||value[0]===1;
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
    let current=element(hit[0]),matched=false,hitChainProof=false,expectedParent=null;
    // A bound content target can only be hit by its proven renderer subtree.
    // An unlisted owner-PID element cannot enter it through a one-way AXParent
    // reference. The later, validated embedded-root -> owner ascent is distinct.
    if(input.embeddedBinding&&registered(ref)&&underContent(registered(ref).path))
      strictPid(current,input.embeddedBinding.pid);
    const ancestors=[];
    for(let depth=0;depth<=input.maxDepth;depth++) {
      check();
      if(expectedParent&&!same(current,expectedParent)) fail('DirectAXEmbeddedStructureChanged');
      const known=hitChainProof&&input.embeddedBinding?registered(current):null;
      // Only this pointer-only ascent shares its first complete chain proof.
      // Every next edge/window is fresh, and its root is checked again at exit.
      if(known) verifyEmbeddedEdge(known); else verifyPid(current);
      hitChainProof=true;
      const observed=embeddedState?registered(current):null;
      expectedParent=observed?observed.parent:null;
      if(ancestors.some(parent=>same(parent,current))) fail('DirectAXTreeCycle');
      ancestors.push(current);
      matched=matched||same(current,ref);
      if(same(current,window)) {
        if(input.embeddedBinding) embeddedRoot();
        return {visible:matched,evidence:matched?'owned-hit-test':'other-hit'};
      }
      if(same(current,application)) {
        if(input.embeddedBinding) embeddedRoot();
        return {visible:false,evidence:'other-window'};
      }
      const parent=read(current,'AXParent',true);
      if(parent===null) {
        if(input.embeddedBinding) embeddedRoot();
        return {visible:false,evidence:'unverified-hit'};
      }
      current=element(parent);
    }
    fail('DirectAXTreeBudgetExceeded');
  }
  function validPid(value) {return Number.isInteger(value)&&value>0&&value<2147483648;}
  function pathSame(a,b) {return a.join('/')===b.join('/');}
  function underRoot(path) {
    const root=input.embeddedBinding.rootPath;
    return path.length>=root.length&&root.every((value,index)=>path[index]===value);
  }
  function underContent(path) {
    const root=embeddedState&&embeddedState.contentPath;
    return !!root&&path.length>=root.length&&root.every((value,index)=>path[index]===value);
  }
  function strictPid(ref,expected) {
    check();operation='read-pid';
    const out=Ref(),error=Number($.AXUIElementGetPid(ref,out));
    if(error!==0||Number(out[0])!==expected) fail('DirectAXEmbeddedStructureChanged',null,error);
  }
  function relation(ref,name,target) {
    check();operation='verify-embedded-relation';
    const out=Ref(),error=Number($.AXUIElementCopyAttributeValue(ref,$(name),out));
    if(error!==0||!same(typed(out[0],$.AXUIElementGetTypeID),target))
      fail('DirectAXEmbeddedStructureChanged',name,error);
  }
  function childEdge(parent,ref,index,count) {
    const children=array(read(parent,'AXChildren'),input.maxNodes);
    if(children.length!==count||!children[index]||!same(children[index],ref)||
       children.filter(child=>same(child,ref)).length!==1) fail('DirectAXEmbeddedStructureChanged','AXChildren');
  }
  function embeddedRoot() {
    rootChecks++;
    const state=embeddedState,binding=input.embeddedBinding;
    if(!state) fail('DirectAXEmbeddedStructureChanged');
    strictPid(state.application,input.pid);strictPid(state.window,input.pid);strictPid(state.parent,input.pid);
    strictPid(state.root,binding.pid);
    const windows=array(read(state.application,'AXWindows'),8);
    if(!windows[binding.rootPath[0]]||!same(windows[binding.rootPath[0]],state.window))
      fail('DirectAXEmbeddedStructureChanged','AXWindows');
    let owned=state.window;
    for(let i=1;i<binding.rootPath.length-1;i++) {
      const siblings=array(read(owned,'AXChildren'),input.maxNodes),child=siblings[binding.rootPath[i]];
      if(!child) fail('DirectAXEmbeddedStructureChanged','AXChildren');
      owned=element(child);strictPid(owned,input.pid);
    }
    if(!same(owned,state.parent)) fail('DirectAXEmbeddedStructureChanged','AXParent');
    childEdge(state.parent,state.root,binding.rootPath[binding.rootPath.length-1],binding.parentChildCount);
    relation(state.root,'AXParent',state.parent);relation(state.root,'AXWindow',state.window);
    strictPid(state.application,input.pid);strictPid(state.root,binding.pid);
    if(state.content) verifyContentScope();
  }
  function registered(ref) {return embeddedNodes.find(node=>same(node.ref,ref));}
  function verifyEmbeddedEdge(node) {
    check();strictPid(node.ref,input.embeddedBinding.pid);
    relation(node.ref,'AXWindow',embeddedState.window);
    relation(node.ref,'AXParent',node.parent);
    childEdge(node.parent,node.ref,node.path[node.path.length-1],node.parentChildCount);
  }
  function validateEmbedded(node) {
    embeddedRoot();
    let current=node;
    for(let depth=0;depth<=input.maxDepth;depth++) {
      verifyEmbeddedEdge(current);
      if(same(current.ref,embeddedState.root)) return;
      current=registered(current.parent);
      if(!current) fail('DirectAXEmbeddedStructureChanged');
    }
    fail('DirectAXTreeBudgetExceeded');
  }
  function acceptEmbedded(ref,context) {
    if(!embeddedState) {
      if(!context||!pathSame(context.path,input.embeddedBinding.rootPath)||
         context.childCount!==input.embeddedBinding.parentChildCount) fail('DirectAXEmbeddedStructureChanged');
      for(const ancestor of context.ancestors) strictPid(ancestor,input.pid);
      embeddedState={root:ref,parent:context.ancestors[context.ancestors.length-1],window:context.window,
        application:context.application,rootRole:null,wrappers:[],content:null,contentPath:null};
      embeddedRoot();
    }
    let node=registered(ref);
    if(context) {
      if(!underRoot(context.path)) fail('DirectAXEmbeddedStructureChanged');
      maximumEmbeddedDepth=Math.max(maximumEmbeddedDepth,context.path.length-input.embeddedBinding.rootPath.length);
      const parent=context.ancestors[context.ancestors.length-1];
      if(!same(ref,embeddedState.root)&&!registered(parent)) fail('DirectAXEmbeddedStructureChanged');
      if(node&&(!pathSame(node.path,context.path)||!same(node.parent,parent))) fail('DirectAXEmbeddedStructureChanged');
      if(!node) {
        node={ref:ref,parent:parent,path:context.path,parentChildCount:context.childCount};
        if(embeddedNodes.length>=input.maxNodes) fail('DirectAXTreeBudgetExceeded');
        embeddedNodes.push(node);
      }
    } else if(!node) {
      // Hit testing can return an as-yet unvisited descendant. Read pointers
      // only until it reaches a registered ancestor; no foreign metadata.
      const pending=[],seen=[];let current=ref;
      while(!registered(current)) {
        check();if(pending.length>=input.maxDepth||seen.some(item=>same(item,current))) fail('DirectAXTreeBudgetExceeded');
        seen.push(current);strictPid(current,input.embeddedBinding.pid);
        relation(current,'AXWindow',embeddedState.window);
        const parent=element(read(current,'AXParent'));
        strictPid(parent,input.embeddedBinding.pid);
        const siblings=array(read(parent,'AXChildren'),input.maxNodes),indexes=[];
        for(let i=0;i<siblings.length;i++) if(same(siblings[i],current)) indexes.push(i);
        if(indexes.length!==1) fail('DirectAXEmbeddedStructureChanged');
        pending.push({ref:current,parent:parent,index:indexes[0],count:siblings.length});current=parent;
      }
      let ancestor=registered(current);validateEmbedded(ancestor);
      for(const item of pending.reverse()) {
        const path=ancestor.path.concat(item.index);
        if(path.length>42||!underRoot(path)) fail('DirectAXTreeBudgetExceeded');
        ancestor={ref:item.ref,parent:item.parent,path:path,parentChildCount:item.count};
        if(embeddedNodes.length>=input.maxNodes) fail('DirectAXTreeBudgetExceeded');
        embeddedNodes.push(ancestor);
      }
      node=registered(ref);
    }
    validateEmbedded(node);
  }
  function safeRole(role) {return ['AXWebArea','AXGroup','AXScrollArea','AXUnknown'].includes(role)?role:'Other';}
  function discoverContent(ref,role,context) {
    const state=embeddedState;
    state.rootRole=safeRole(role);
    let current=ref,path=context.path,ancestors=context.ancestors;
    while(role!=='AXWebArea') {
      check();
      const step={ref:current,path:path,role:safeRole(role),childCount:null,childrenEvidence:null,
        parentMatches:true,windowMatches:true};
      state.wrappers.push(step);
      if(role!=='AXGroup') fail('DirectAXEmbeddedWrapperRoleRejected','AXRole');
      const result=children(current,role);
      step.childCount=result.values.length;step.childrenEvidence=result.evidence;
      if(result.values.length!==1) fail('DirectAXEmbeddedWrapperBranch','AXChildren');
      if(path.length>=41||state.wrappers.length>=input.maxDepth) fail('DirectAXTreeBudgetExceeded');
      ancestors=ancestors.concat([current]);path=path.concat(0);
      current=element(result.values[0]);
      if(ancestors.some(parent=>same(parent,current))) fail('DirectAXTreeCycle');
      verifyPid(current,{application:state.application,window:state.window,path:path,ancestors:ancestors,childCount:1});
      role=string(read(current,'AXRole')); // Structural roles only; never wrapper labels or values.
    }
    state.content=current;state.contentPath=path;
    verifyContentScope();
  }
  function verifyContentScope() {
    const state=embeddedState;
    for(let i=0;i<state.wrappers.length;i++) {
      const step=state.wrappers[i],next=i+1<state.wrappers.length?state.wrappers[i+1].ref:state.content;
      strictPid(step.ref,input.embeddedBinding.pid);
      if(string(read(step.ref,'AXRole'))!=='AXGroup') fail('DirectAXEmbeddedStructureChanged','AXRole');
      childEdge(step.ref,next,0,1);
      strictPid(next,input.embeddedBinding.pid);
      relation(next,'AXParent',step.ref);relation(next,'AXWindow',state.window);
    }
    strictPid(state.content,input.embeddedBinding.pid);
    if(string(read(state.content,'AXRole'))!=='AXWebArea') fail('DirectAXEmbeddedStructureChanged','AXRole');
  }
  function contentScope() {
    if(!embeddedState||!embeddedState.content) return null;
    return {contentRootRole:'AXWebArea',contentRootPath:embeddedState.contentPath,
      wrapperChain:embeddedState.wrappers.map(node=>({path:node.path,role:node.role,childCount:node.childCount}))};
  }
  function embeddedProof(verified) {
    if(!input.embeddedBinding) return null;
    return {verified:verified,applicationPid:input.pid,embeddedPid:input.embeddedBinding.pid,
      rootPath:input.embeddedBinding.rootPath,rootRole:embeddedState?embeddedState.rootRole:null,
      contentRootRole:embeddedState&&embeddedState.content?'AXWebArea':null,
      contentRootPath:embeddedState?embeddedState.contentPath:null,
      wrapperChain:embeddedState?embeddedState.wrappers.map(node=>({path:node.path,role:node.role,childCount:node.childCount,
        childrenEvidence:node.childrenEvidence,parentMatches:node.parentMatches,windowMatches:node.windowMatches})):[]};
  }
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
    if(error===0&&input.embeddedBinding&&Number(pid[0])===input.embeddedBinding.pid) {
      acceptEmbedded(application,context);return;
    }
    if(error===0&&Number(pid[0])===input.pid&&context&&embeddedState&&underRoot(context.path))
      fail('DirectAXEmbeddedStructureChanged');
    if(error!==0||Number(pid[0])!==input.pid) {
      // Reserve the record before any diagnostic revalidation can fail again.
      if(pidMismatch===null) {
        pidMismatch={expectedPid:input.pid,actualPid:null,origin:'other',status:'pid-read-failed'};
        if(error===0) pidMismatch=mismatchStructure(application,Number(pid[0]),input.embeddedBinding?null:context);
      }
      fail('DirectAXProcessMismatch',null,error);
    }
  }
  function open() {
    stage='preflight';
    if(!Number.isInteger(input.pid)||input.pid<=0||input.maxNodes!==1000||input.maxDepth!==40||
       !Number.isFinite(limit)||limit<=0) fail('InvalidDirectAXInput');
    if(input.embeddedBinding) {
      const binding=input.embeddedBinding,path=binding.rootPath;
      if(!validPid(binding.pid)||binding.pid===input.pid||!Array.isArray(path)||path.length<2||path.length>42||
         path[0]>=8||path.some(i=>!Number.isInteger(i)||i<0||i>=input.maxNodes)||
         !Number.isInteger(binding.parentChildCount)||binding.parentChildCount<=path[path.length-1]||
         binding.parentChildCount>input.maxNodes) fail('InvalidDirectAXInput');
    }
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
          if(embeddedState&&same(ref,embeddedState.root))
            discoverContent(ref,role,{path:path,ancestors:ancestors});
          const structureOnly=!!input.embeddedBinding&&underRoot(path)&&!underContent(path);
          if(structureOnly&&!embeddedState.wrappers.some(node=>same(node.ref,ref)&&pathSame(node.path,path)))
            fail('DirectAXEmbeddedStructureChanged');
          const control=interactive.includes(role)&&!editableAncestor&&!structureOnly;
          const labelled=(control||semantic.includes(role))&&!editableAncestor&&!structureOnly,info=labelled?metadata(ref,role):{};
          inWeb=inWeb||role==='AXWebArea';
          const node={path:path,role:role,name:editableAncestor||structureOnly?'':labelled?info.name:role==='AXWebArea'?
            string(read(ref,'AXTitle',true))||string(read(ref,'AXDescription',true)):'',
            text:!editableAncestor&&role==='AXStaticText'?info.staticText:'',identifier:info.identifier||'',
            enabled:control&&info.enabled===true,visible:false,visibilityEvidence:'not-observed',insideWebContent:inWeb&&!structureOnly,
            editableAncestor:editableAncestor,structureOnly:structureOnly,password:info.password===true,actions:labelled&&!info.password?actions(ref):[],
            valueSettable:control&&!info.password&&['AXTextField','AXTextArea'].includes(role)?settable(ref):null};
          node.scrollToVisible=node.actions.includes('AXScrollToVisible');
          // A visible window is not proof that an individual control/text is
          // exposed. Read-only, application-scoped hit testing also excludes
          // clipped, offscreen and occluded elements; it never clicks a point.
          if(inWeb&&!editableAncestor&&(labelled||role==='AXStaticText')&&!info.password) {
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
      if(input.embeddedBinding) embeddedRoot();
    } catch(error) {
      snapshot.truncated=true;
      snapshot.errorStage=error&&error.safe?error.stage:stage;
      snapshot.diagnostic=error&&error.safe?{code:error.code,operation:error.operation,attribute:error.attribute,axError:error.axError,
        countKind:error.countKind,countValue:error.countValue}:
        {code:'DirectAXTreeUnavailable',operation:operation,attribute:null,axError:null};
    }
    snapshot.pidMismatch=pidMismatch;
    snapshot.embeddedAXProof=embeddedProof(!snapshot.truncated&&!!embeddedState&&!!embeddedState.content);
    snapshot.axReadCounts={checks:reads,rootProofs:rootChecks,maximumEmbeddedDepth:maximumEmbeddedDepth};
    snapshot.elapsedMs=Date.now()-began;
    return {snapshot:snapshot,application:application,windows:windowRefs,elements:elements};
  }
  function resolve(application,path) {
    const windows=array(read(application,'AXWindows'),8);
    if(!windows[path[0]]) fail('DirectAXControlChanged');
    let ref=element(windows[path[0]]),inWeb=false,editableAncestor=false;
    verifyPid(ref);
    if(!visible(application,ref)) fail('DirectAXControlInvisible');
    const window=ref;
    for(let i=1;i<path.length;i++) {
      const parentRole=string(read(ref,'AXRole'));
      editableAncestor=editableAncestor||editable.includes(parentRole);
      const nested=children(ref,parentRole).values;
      if(!nested[path[i]]) fail('DirectAXControlChanged');
      ref=element(nested[path[i]]);
      verifyPid(ref);
      inWeb=inWeb||string(read(ref,'AXRole'))==='AXWebArea';
    }
    if(!inWeb) fail('DirectAXControlOutsideWeb');
    if(editableAncestor) fail('DirectAXEditableDescendant');
    return {ref:ref,window:window};
  }
  function press() {
    stage='validate-control';
    const expected=input.selector,path=expected&&expected.path;
    const op=input.operation,scroll=op==='scroll',setting=op==='set';
    const pressRoles=['AXButton','AXLink','AXMenuItem','AXCheckBox','AXRadioButton','AXPopUpButton'];
    const roles=setting?['AXTextField','AXTextArea']:scroll?interactive.concat(semantic):pressRoles;
    if(!['press','set','scroll'].includes(op)||!Array.isArray(path)||path.length<2||path.length>42||
       path.some(i=>!Number.isInteger(i)||i<0||i>=1000)||
       !roles.includes(expected.role)||typeof expected.name!=='string'||!expected.name||expected.name.length>300||
       (setting&&(typeof input.value!=='string'||input.value.length>4096)))
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
    if(input.embeddedBinding&&(!input.expectedContentScope||
       JSON.stringify(input.expectedContentScope)!==JSON.stringify(contentScope())))
      fail('DirectAXEmbeddedContentScopeChanged');
    const candidates=snapshot.windows.filter(w=>w.visible).flatMap(w=>w.nodes).filter(n=>n.insideWebContent&&
      (scroll||(n.visible&&n.enabled))&&!n.password&&!n.editableAncestor&&n.role===expected.role&&n.name===expected.name);
    if(candidates.length!==1) fail('DirectAXControlAmbiguous');
    const found=candidates[0];
    const action=scroll?'AXScrollToVisible':'AXPress';
    if(found.path.join('/')!==path.join('/')||found.identifier!==expected.identifier)
      fail('DirectAXControlChanged');
    if(setting?found.valueSettable!==true:!found.actions.includes(action)) fail('DirectAXOperationUnsupported');
    stage='resolve-control';
    const current=resolve(view.application,path),held=view.elements.get(path.join('/'));
    if(!same(current.ref,held)||!same(current.window,view.windows[path[0]])) fail('DirectAXControlChanged');
    stage='validate-control';
    const role=string(read(current.ref,'AXRole')),info=metadata(current.ref,role);
    if(role!==expected.role||info.password||(!scroll&&!info.enabled)||info.name!==expected.name||info.identifier!==expected.identifier||
       !visible(view.application,current.window)) fail('DirectAXControlChanged');
    if(setting?!settable(current.ref):!actions(current.ref).includes(action)) fail('DirectAXOperationUnsupported');
    if(!scroll&&!exposed(view.application,current.window,bounds(current.window),current.ref).visible) fail('DirectAXControlInvisible');
    verifyPid(view.application);verifyPid(current.ref);check();stage='perform-action';
    operation=setting?'set-value':scroll?'scroll-to-visible':'press';
    // Value is submitted once from stdin and never read back or returned.
    const error=Number(setting?$.AXUIElementSetAttributeValue(current.ref,$('AXValue'),$(input.value)):
      $.AXUIElementPerformAction(current.ref,$(action)));
    if(error!==0) fail('DirectAXCallFailed',null,error);
    if(input.embeddedBinding) embeddedRoot();
    const result={performed:true,operation:op};
    if(input.embeddedBinding) result.embeddedAXProof=embeddedProof(!!embeddedState&&!!embeddedState.content);
    return result;
  }
  return {inspect:inspect,press:press};
}
