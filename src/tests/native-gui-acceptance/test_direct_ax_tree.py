#!/usr/bin/env python3
"""Pure CF/AX fixtures only: no native accessibility or permission calls."""
import importlib.util
import json
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch
from types import SimpleNamespace

HERE = Path(__file__).resolve().parent


def load(name, filename):
    spec = importlib.util.spec_from_file_location(name, HERE / filename)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


probe = load("direct_tree_probe_test", "probe.py")
flow = load("direct_tree_flow_test", "flow.py")
runner = load("direct_tree_runner_test", "run-probe.py")
APP = {"role": "unified", "rid": "osx-x64", "exe": Path("/fixture/Bakabase"), "nativeBackend": "macos-direct-ax"}
IDENTITY = {"pid": 42, "started": "owned-start", "executable": "/fixture/Bakabase"}

FIXTURE = r'''
const fs=require('fs'),vm=require('vm'),directory=process.argv[1],options=JSON.parse(process.argv[2]);
const source=['macos-cf-values.js','macos-ax-provider.js',options.action?'macos-ax-action.js':'macos-ax-snapshot.js']
 .map(name=>fs.readFileSync(directory+'/'+name,'utf8')).join('\n');
function Ref(target){if(!(this instanceof Ref))return new Ref(target);this.target=target}
const value=v=>v instanceof Ref?v.target:v;
const box=v=>({kind:typeof v==='string'?7:typeof v==='boolean'?21:19,value:v});
const node=(id,role,attrs={})=>({kind:99,id,role,attrs,children:[],pid:42});
const close=node('close','AXButton',{AXTitle:'Close',AXIdentifier:'close',AXEnabled:true});
const text=node('text','AXStaticText',{AXValue:'No matching resources'});
const inputNode=node('input','AXTextField',{AXTitle:'Address',AXEnabled:true,AXValue:'DO-NOT-READ'});
if(options.secureInput)inputNode.attrs.AXSubrole='AXSecureTextField';
const secure=node('secure','AXTextField',{AXSubrole:'AXSecureTextField',AXEnabled:true,AXValue:'SECRET'});
const web=node('web','AXWebArea',{AXTitle:'Bakabase'});web.children=[close,text,inputNode,secure];
const window=node('window','AXWindow',{AXTitle:'Bakabase',AXMinimized:!!options.minimized});window.children=[web];
const app=node('app','AXApplication',{AXHidden:!!options.hidden});app.windows=[window];
const wrappers=[];
for(let i=(options.wrapperDepth||0)-1;i>=0;i--){const group=node('wrapper'+i,'AXGroup',
 {AXTitle:'WRAPPER-CONTENT-MUST-NOT-BE-READ',AXIdentifier:'WRAPPER-PRIVATE-ID',AXEnabled:true});
 group.children=[window.children[0]];window.children=[group];wrappers.unshift(group);}
if(options.wrapperEmpty)wrappers[0].children=[];
if(options.wrapperBranch)wrappers[0].children.push(node('wrapper-sibling','AXGroup',{AXTitle:'UNSCOPED-SIBLING'}));
if(options.wrapperCycle)wrappers[0].children=[wrappers[0]];
if(options.ownedWebAncestor){const owned=node('owned-web','AXWebArea',{AXTitle:'Owned shell'});
 owned.children=window.children;window.children=[owned];}
if(options.embeddedDescendantHit)close.children=[node('actual-child-hit','AXStaticText',{AXValue:'Nested button label'})];
const region=node('region','AXGroup',{AXTitle:'Connection settings'});
if(options.regionGroup)web.children.push(region);
if(options.duplicateInput)web.children.push(node('duplicate-input','AXTextField',{AXTitle:'Address',AXEnabled:true}));
if(options.embeddedRootRole)web.role=options.embeddedRootRole;
if(options.editableDescendants) {
 if(options.comboBox)inputNode.role='AXComboBox';
 const group=node('editable-group','AXGroup');
 group.children=[node('editable-text','AXStaticText',{AXValue:'EDITABLE-CONTENT'}),
  node('editable-button','AXButton',{AXTitle:'EDITABLE-LABEL',AXIdentifier:'EDITABLE-ID',AXEnabled:true})];
 (options.secureDescendants?secure:inputNode).children=[group];
}
if(options.foreignNonWeb) {const foreign=node('foreign-static','AXStaticText',{AXValue:'OTHER-PROCESS-CONTENT'});foreign.pid=900;window.children.push(foreign);}
if(options.hiddenContainer) {const group=node('hidden-container','AXGroup');group.children=[node('hidden-duplicate','AXButton',{AXTitle:'Close',AXEnabled:true})];web.children.push(group);}
if(options.emptyGroup)web.children.push(node('empty-group','AXGroup'));
if(options.duplicate)web.children.push(node('duplicate','AXButton',{AXTitle:'Close',AXIdentifier:'another',AXEnabled:true}));
if(options.deep){let parent=web;for(let i=0;i<42;i++){const n=node('depth'+i,'AXGroup');parent.children=[n];parent=n;}}
if(options.cycle)web.children=[window];
if(options.many)web.children=Array.from({length:1001},(_,i)=>node('n'+i,'AXGroup'));
if(options.foreignPid)app.pid=43;
const reads=[],calls=[],seen={};let presses=0,sets=0,scrolls=0,submittedValueMatched=false,clock=0,pidReads=0,hitTarget=null;
let hitEdgeArmed=false,hitParentReads=0;
function parents(n,parent=null,visited=new Set()) {
 if(visited.has(n))return;visited.add(n);n.parent=parent;
 for(const c of n.windows||n.children)parents(c,n,visited);
}
parents(app);
if(options.embedded){const assigned=new Set();const assign=n=>{if(assigned.has(n))return;assigned.add(n);
 n.pid=900;for(const child of n.children)assign(child)};
 assign(options.ownedWebAncestor?window.children[0].children[0]:window.children[0]);}
if(options.wrapperSecondPid)web.pid=901;
if(options.secondEmbeddedPid)inputNode.pid=901;
const native=s=>box(s);
Object.assign(native,{
 AXIsProcessTrusted:()=>{calls.push('trust');return !options.untrusted},
 AXUIElementCreateApplication:pid=>{if(pid!==42)throw Error('unowned');calls.push('create');return new Ref(app)},
 AXUIElementGetTypeID:()=>99,CFStringGetTypeID:()=>7,CFArrayGetTypeID:()=>19,CFBooleanGetTypeID:()=>21,
 AXValueGetTypeID:()=>98,AXValueGetType:ref=>value(ref).type,
 AXValueGetValue:(ref,type,buffer)=>{const v=value(ref);if(v.type!==type)return false;buffer[0]=v.value[0];buffer[1]=v.value[1];return true},
 calloc:()=>[0,0],free:()=>{},
 CFGetTypeID:v=>v instanceof Ref?18:v.kind,CFBooleanGetValue:v=>v.value,
 CFArrayGetCount:v=>{if(options.genericFailure==='array-count')throw Error('SECRET-RAW-ERROR');return v.value.length},
 CFArrayGetValueAtIndex:(v,i)=>{if(options.genericFailure==='array-item')throw Error('SECRET-RAW-ERROR');return new Ref(v.value[i])},CFEqual:(a,b)=>a===b,
 AXUIElementSetMessagingTimeout:(ref,time)=>{if(time!==0.5)throw Error('timeout changed');
  if(options.strictTypedArgument&&ref instanceof Ref)throw Error('Generic pointer is not a typed AX argument');
  if(options.genericFailure==='element-timeout'&&value(ref)===window)throw Error('SECRET-RAW-ERROR');return 0},
 AXUIElementGetPid:(ref,out)=>{pidReads++;out[0]=options.pidChanged&&pidReads>=3?43:value(ref).pid;
  if(options.foreignPidReadError&&value(ref).id==='foreign-static')return -25202;return 0},
 AXUIElementCopyAttributeValue:(ref,key,out)=>{
  const n=value(ref),name=key.value,k=n.id+':'+name;reads.push(k);seen[k]=(seen[k]||0)+1;
  if(options.genericFailure===name)throw Error('SECRET-RAW-ERROR');
  if(options.wrapperRoleChanged&&n===wrappers[0]&&name==='AXRole'&&seen[k]>=2){out[0]=new Ref(box('AXTextField'));return 0;}
  if(options.wrapperMovedAfterMetadata&&n===web&&name==='AXTitle')wrappers[wrappers.length-1].children=[node('new-web','AXWebArea')];
  if(options.secureAfterRead&&n===inputNode&&name==='AXSubrole'&&seen[k]>=2){out[0]=new Ref(box('AXSecureTextField'));return 0;}
  if(name==='AXValue'&&n.role!=='AXStaticText')throw Error('Editable value requested');
  if(options.missingChildren&&n===web&&name==='AXChildren')return -25204;
  if(name==='AXChildren'&&((options.noChildrenValue&&n===web)||(options.emptyGroup&&n.id==='empty-group')))return -25212;
  if(options.unsupportedLeaf&&n.role==='AXStaticText'&&name==='AXChildren')return -25205;
  if(options.hiddenContainer&&n.id==='hidden-container'&&name==='AXChildren')return -25205;
  if(options.changedControl&&n===close&&name==='AXTitle'&&seen[k]>=2){out[0]=new Ref(box('Changed'));return 0;}
  if(options.replacedControl&&n===web&&name==='AXChildren'&&seen[k]>=2){
   out[0]=new Ref(box([node('replacement','AXButton',{AXTitle:'Close',AXIdentifier:'close',AXEnabled:true}),text,inputNode,secure]));return 0;
  }
  if(options.hiddenAfterRead&&n===app&&name==='AXHidden'&&seen[k]>=2){out[0]=new Ref(box(true));return 0;}
  if(options.foreignEdgeChanged&&n===window&&name==='AXChildren'&&seen[k]>=2){out[0]=new Ref(box([web]));return 0;}
  if(options.foreignWindowChanged&&n===app&&name==='AXWindows'&&seen[k]>=2){out[0]=new Ref(box([]));return 0;}
  if(n.id==='foreign-static'&&(name==='AXParent'||name==='AXWindow')) {
   if(options.foreignStructureError)return options.foreignStructureError;
   if(options.foreignStructureType){out[0]=new Ref(box('FOREIGN-POINTER-CONTENT'));return 0;}
   if(options.foreignStructureSlow)clock+=24000;
   if(options.foreignPidChanged&&name==='AXWindow')n.pid=901;
   out[0]=new Ref(options.foreignStructureMismatch?app:window);return 0;
  }
  if(name==='AXParent'){
   if(hitEdgeArmed&&n===web){hitParentReads++;if(options.hitEdgeChanged&&hitParentReads===3){out[0]=new Ref(app);return 0;}
    if(options.hitRootChanged&&hitParentReads===4)window.children=[];}
   if(!n.parent)return -25205;out[0]=new Ref(options.embeddedWrongParent&&n===web?app:n.parent);return 0;}
  if(name==='AXWindow'){out[0]=new Ref(options.embeddedWrongWindow&&n===web?app:window);return 0;}
  if(name==='AXPosition'||name==='AXSize') {
   if(options.unknownGeometry&&n===close)return -25205;
   const position=n===window?[0,0]:(options.offscreen&&n===close)||(options.offscreenText&&n===text)||(options.offscreenInput&&n===inputNode)||(options.offscreenRegion&&n===region)?[2000,20]:[10,20];
   const size=n===window?[1000,800]:options.zeroSize&&n===close?[0,20]:[40,20];
   hitTarget=n;out[0]=new Ref({kind:98,type:name==='AXPosition'?1:2,value:name==='AXPosition'?position:size});return 0;
  }
  let v=name==='AXRole'?n.role:name==='AXChildren'?n.children:name==='AXWindows'?n.windows:n.attrs[name];
  if(v===undefined)return -25205;
  if(Array.isArray(v))v=v.map(item=>item.kind===99?item:box(item));
  out[0]=new Ref(box(v));return 0;
 },
 AXUIElementCopyActionNames:(ref,out)=>{calls.push(value(ref).id+':actions');const names=value(ref)===secure?[]:['AXPress'];
  if(options.scrollSupported)names.push('AXScrollToVisible');out[0]=new Ref(box(names.map(box)));return 0},
 AXUIElementIsAttributeSettable:(ref,attribute,out)=>{calls.push(value(ref).id+':settable');if(attribute.value!=='AXValue')throw Error('Wrong attribute');
  if(options.settableError)return -25204;out[0]=!options.readOnlyInput;return 0},
 AXUIElementSetAttributeValue:(ref,attribute,inputValue)=>{if(value(ref)!==inputNode||attribute.value!=='AXValue')throw Error('Wrong set');
  if(options.setError)return -25204;sets++;submittedValueMatched=inputValue.value==='TEST-VALUE-PRIVATE';return 0},
 AXUIElementGetAttributeValueCount:(ref,attribute,out)=>{
  if(attribute.value!=='AXChildren')throw Error('Unexpected count attribute');
  calls.push(value(ref).id+':children-count');
  if(options.childCountError)return options.childCountError;
  if(options.pidAfterChildCount)value(ref).pid=900;
  out[0]=options.childCountOverride===undefined?value(ref).children.length:options.childCountOverride;
  if(options.childCountSpecial==='undefined')out[0]=undefined;
  if(options.childCountSpecial==='NaN')out[0]=NaN;
  if(options.childCountSpecial==='Infinity')out[0]=Infinity;
  if(options.childCountSpecial==='-Infinity')out[0]=-Infinity;
  return 0;
 },
 AXUIElementCopyElementAtPosition:(application,x,y,out)=>{
  if(value(application)!==app||x<0||x>1000||y<0||y>800)throw Error('Unowned hit test');
  if(options.noHit)return -25212;
  let hit=hitTarget;
  if((options.hitEdgeChanged||options.hitRootChanged)&&hit===close){hitEdgeArmed=true;hitParentReads=0;}
  if(options.occluded&&hit===close)hit=text;
  if(options.descendantHit&&hit===close){hit=node('child-hit','AXStaticText');hit.parent=close;}
  if(options.embeddedDescendantHit&&hit===close)hit=close.children[0];
  if(options.foreignHit){hit=node('foreign','AXButton');hit.pid=900;}
  if(options.unlistedOwnedHit){hit=node('unlisted-owned-hit','AXButton');hit.pid=42;hit.parent=close;}
  out[0]=new Ref(hit);return 0;
 },
 AXUIElementPerformAction:(ref,action)=>{if(action.value==='AXScrollToVisible'){
   if(!options.scrollSupported||![close,region,inputNode,text].includes(value(ref)))throw Error('Wrong scroll');
   if(options.scrollError)return -25204;scrolls++;return 0;
  }if(value(ref)!==close||action.value!=='AXPress')throw Error('wrong action');
  if(options.pressError)return -25204;presses++;if(options.embeddedMoveAfterAction)window.children=[];return 0}
});
const input={pid:42,maxNodes:1000,maxDepth:40,readBudgetMs:24000,operation:options.operation||'press',
 selector:{path:[0,0,0],role:'AXButton',name:'Close',identifier:'close'}};
if(options.target==='input')input.selector={path:[0,0,2],role:'AXTextField',name:'Address',identifier:''};
if(options.target==='region')input.selector={path:[0,0,4],role:'AXGroup',name:'Connection settings',identifier:''};
if(options.target==='text')input.selector={path:[0,0,1],role:'AXStaticText',name:'No matching resources',identifier:''};
if(options.inputValue!==undefined)input.value=options.inputValue;
if(options.binding)input.embeddedBinding={pid:900,rootPath:options.ownedWebAncestor?[0,0,0]:[0,0],parentChildCount:options.bindingCount||1};
if(options.bindingPath)input.embeddedBinding.rootPath=options.bindingPath;
if(options.ownedWebAncestor)input.selector.path=[0,0].concat(input.selector.path.slice(1));
if(options.wrapperDepth)input.selector.path=input.selector.path.slice(0,2).concat(Array(options.wrapperDepth).fill(0),input.selector.path.slice(2));
if(options.binding&&!options.missingExpectedContentScope){
 const depth=options.expectedWrapperDepth===undefined?(options.wrapperDepth||0):options.expectedWrapperDepth;
 input.expectedContentScope={contentRootRole:'AXWebArea',contentRootPath:input.embeddedBinding.rootPath.concat(Array(depth).fill(0)),
  wrapperChain:Array.from({length:depth},(_,i)=>({path:input.embeddedBinding.rootPath.concat(Array(i).fill(0)),role:'AXGroup',childCount:1}))};
}
const output=JSON.parse(vm.runInNewContext('const input='+JSON.stringify(input)+';\n'+source,{
 $:native,Ref,Date:{now:()=>{clock+=options.slow?1000:1;return clock}},
 ObjC:{import:name=>{if(name!=='ApplicationServices')throw Error();},bindFunction:(name)=>{if(!['calloc','free'].includes(name))throw Error();},
  castRefToObject:ref=>value(ref),castObjectToRef:v=>new Ref(v),unwrap:v=>v.value}
}));
process.stdout.write(JSON.stringify({output,presses,sets,scrolls,submittedValueMatched,reads,calls}));
'''


@unittest.skipUnless(shutil.which("node"), "Node is required for pure native API fixtures")
class ProviderFixtures(unittest.TestCase):
    def run_fixture(self, **options):
        result = subprocess.run([shutil.which("node"), "-e", FIXTURE, str(HERE), json.dumps(options)],
                                capture_output=True, text=True, timeout=5, check=True)
        return json.loads(result.stdout)

    def test_complete_owned_tree_omits_every_editable_value_and_never_invokes_an_action(self):
        result = self.run_fixture()
        snapshot = result["output"]
        self.assertTrue(snapshot["enabled"])
        self.assertFalse(snapshot["truncated"])
        self.assertTrue(probe.summarize(snapshot)["capabilityPassed"])
        self.assertEqual(0, result["presses"])
        self.assertNotIn("input:AXValue", result["reads"])
        self.assertNotIn("secure:AXValue", result["reads"])
        self.assertNotIn("SECRET", json.dumps(snapshot))
        self.assertIn("text:AXValue", result["reads"])

    def test_array_elements_use_validated_cf_objects_for_typed_ax_calls(self):
        snapshot = self.run_fixture(strictTypedArgument=True)["output"]
        self.assertFalse(snapshot["truncated"])
        self.assertTrue(probe.summarize(snapshot)["capabilityPassed"])
        action = self.run_fixture(strictTypedArgument=True, action=True)
        self.assertTrue(action["output"]["performed"])
        self.assertEqual(1, action["presses"])

    def test_generic_bridge_failures_retain_only_fixed_operation_without_raw_error(self):
        for fault, expected in (("array-count", "array-count"), ("array-item", "array-item"),
                                ("element-timeout", "element-timeout"), ("AXRole", "read-role"),
                                ("AXHidden", "read-hidden"), ("AXMinimized", "read-minimized"),
                                ("AXPosition", "read-position"), ("AXSize", "read-size")):
            with self.subTest(fault=fault):
                result = self.run_fixture(genericFailure=fault)
                snapshot = result["output"]
                self.assertTrue(snapshot["truncated"])
                self.assertEqual(expected, snapshot["diagnostic"]["operation"])
                self.assertEqual("DirectAXTreeUnavailable", snapshot["diagnostic"]["code"])
                self.assertEqual(expected, probe.sanitize(snapshot)["diagnostic"]["operation"])
                self.assertNotIn("SECRET-RAW-ERROR", json.dumps(snapshot))
                self.assertEqual(0, result["presses"])

    def test_editable_descendants_are_structurally_traversed_but_never_read_as_display_text(self):
        for options in ({}, {"comboBox": True}, {"secureDescendants": True}):
            with self.subTest(options=options):
                result = self.run_fixture(editableDescendants=True, **options)
                snapshot = result["output"]
                self.assertFalse(snapshot["truncated"])
                self.assertNotIn("EDITABLE-", json.dumps(snapshot))
                self.assertNotIn("editable-text:AXValue", result["reads"])
                self.assertNotIn("editable-button:AXTitle", result["reads"])
                self.assertNotIn("editable-button:AXIdentifier", result["reads"])
                nodes = [n for w in snapshot["windows"] for n in w["nodes"] if n["editableAncestor"]]
                self.assertEqual(3, len(nodes))
                self.assertTrue(all(not n["name"] and not n["text"] and not n["identifier"] and
                                    not n["actions"] and not n["enabled"] and not n["visible"] for n in nodes))
                self.assertIn("editable-text:AXChildren", result["reads"])

    def test_foreign_non_web_node_is_rejected_before_its_role_or_value_is_read(self):
        result = self.run_fixture(foreignNonWeb=True)
        self.assertTrue(result["output"]["truncated"])
        self.assertEqual("DirectAXProcessMismatch", result["output"]["diagnostic"]["code"])
        self.assertEqual(["foreign-static:AXParent", "foreign-static:AXWindow"],
                         [read for read in result["reads"] if read.startswith("foreign-static:")])
        self.assertNotIn("OTHER-PROCESS-CONTENT", json.dumps(result["output"]))
        self.assertNotIn("foreign-static:actions", result["calls"])
        diagnostic = probe.sanitize(result["output"])["pidMismatch"]
        self.assertEqual("observed", diagnostic["status"])
        self.assertEqual((42, 900, [0, 1], [0], 1, 2), tuple(diagnostic[k] for k in
                         ("expectedPid", "actualPid", "path", "parentPath", "childIndex", "parentChildCount")))
        for key in ("parentMatches", "windowMatches", "actualPidStable", "edgeStillMatches", "windowStillMatches"):
            self.assertIs(True, diagnostic[key])
        action = self.run_fixture(foreignNonWeb=True, action=True)
        self.assertFalse(action["output"]["performed"])
        self.assertEqual(0, action["presses"])

    def test_foreign_relations_errors_change_and_budget_never_allow_metadata_or_actions(self):
        for options, status in (({"foreignStructureMismatch": True}, "observed"),
                                ({"foreignStructureError": -25204}, "observed"),
                                ({"foreignStructureType": True}, "diagnostic-unavailable"),
                                ({"foreignPidChanged": True}, "pid-changed"),
                                ({"foreignStructureSlow": True}, "budget-exhausted")):
            with self.subTest(options=options):
                result = self.run_fixture(foreignNonWeb=True, **options)
                self.assertTrue(result["output"]["truncated"])
                self.assertEqual("DirectAXProcessMismatch", result["output"]["diagnostic"]["code"])
                self.assertEqual(status, result["output"]["pidMismatch"]["status"])
                self.assertTrue(all(read in ("foreign-static:AXParent", "foreign-static:AXWindow")
                                    for read in result["reads"] if read.startswith("foreign-static:")))
                self.assertEqual(0, result["presses"])
                self.assertNotIn("FOREIGN-POINTER-CONTENT", json.dumps(result["output"]))
                if options.get("foreignStructureMismatch"):
                    self.assertIs(False, result["output"]["pidMismatch"]["parentMatches"])
                if options.get("foreignStructureError"):
                    self.assertEqual(-25204, result["output"]["pidMismatch"]["windowAXError"])
                    self.assertIsNone(result["output"]["pidMismatch"]["windowMatches"])

    def test_changed_owned_edge_window_or_failed_pid_read_never_inspects_foreign_pointers(self):
        for option in ("foreignEdgeChanged", "foreignWindowChanged", "foreignPidReadError"):
            with self.subTest(option=option):
                result = self.run_fixture(foreignNonWeb=True, **{option: True})
                self.assertTrue(result["output"]["truncated"])
                self.assertEqual("DirectAXProcessMismatch", result["output"]["diagnostic"]["code"])
                self.assertFalse(any(read.startswith("foreign-static:") for read in result["reads"]))

    def test_foreign_hit_without_owned_child_edge_has_no_structural_probe(self):
        result = self.run_fixture(foreignHit=True)
        self.assertTrue(result["output"]["truncated"])
        self.assertEqual("other", result["output"]["pidMismatch"]["origin"])
        self.assertFalse(any(read.startswith("foreign:") for read in result["reads"]))

    def test_unsupported_container_children_cannot_hide_duplicate_and_still_press(self):
        result = self.run_fixture(hiddenContainer=True)
        self.assertTrue(result["output"]["truncated"])
        self.assertEqual(-25205, result["output"]["diagnostic"]["axError"])
        self.assertEqual("AXChildren", result["output"]["diagnostic"]["attribute"])
        action = self.run_fixture(action=True, hiddenContainer=True)
        self.assertFalse(action["output"]["performed"])
        self.assertEqual(0, action["presses"])

    def test_no_value_is_empty_only_after_independent_successful_zero_count(self):
        result = self.run_fixture(emptyGroup=True)
        self.assertFalse(result["output"]["truncated"])
        self.assertIn("empty-group:children-count", result["calls"])
        node = result["output"]["windows"][0]["nodes"][-1]
        self.assertEqual("no-value-count-zero", node["childrenEvidence"])
        self.assertEqual(0, node["childCount"])
        saved = probe.sanitize(result["output"])["windows"][0]["nodes"][-1]
        self.assertEqual("no-value-count-zero", saved["childrenEvidence"])
        action = self.run_fixture(emptyGroup=True, action=True)
        self.assertTrue(action["output"]["performed"])
        self.assertEqual(1, action["presses"])

    def test_canonical_cfindex_zero_is_accepted_but_other_strings_never_coerce_to_zero(self):
        result = self.run_fixture(emptyGroup=True, childCountOverride="0")
        self.assertFalse(result["output"]["truncated"])
        node = result["output"]["windows"][0]["nodes"][-1]
        self.assertEqual("decimal-string", node["childrenCountKind"])
        self.assertEqual("no-value-count-zero", node["childrenEvidence"])
        self.assertEqual(0, node["childCount"])
        self.assertEqual("decimal-string", probe.sanitize(result["output"])["windows"][0]["nodes"][-1]["childrenCountKind"])
        for count in ("", " ", " 0", "0 ", "+0", "-0", "00", "0.0", "0e0", "SECRET", True, False, {}, []):
            with self.subTest(count=count):
                action = self.run_fixture(emptyGroup=True, childCountOverride=count, action=True)
                self.assertFalse(action["output"]["performed"])
                self.assertEqual(0, action["presses"])
                self.assertIsNone(action["output"]["diagnostic"]["countValue"])
                self.assertNotIn("SECRET", json.dumps(action["output"]))

    def test_cfindex_positive_count_remains_failure_with_only_bounded_numeric_diagnostic(self):
        for count, expected in (("1", 1), ("1000", 1000), (1, 1), ("1001", None), ("12345", None), (-1, None)):
            with self.subTest(count=count):
                result = self.run_fixture(emptyGroup=True, childCountOverride=count)
                self.assertTrue(result["output"]["truncated"])
                diagnostic = probe.sanitize(result["output"])["diagnostic"]
                self.assertEqual("DirectAXChildrenCountMismatch", diagnostic["code"])
                self.assertEqual(expected, diagnostic["countValue"])

    def test_no_value_positive_unknown_or_failed_count_remains_partial_and_blocks_press(self):
        for options in ({"childCountOverride": 1}, {"childCountOverride": -1}, {"childCountOverride": None},
                        {"childCountOverride": 0.5}, {"childCountError": -25204}, {"childCountError": -25205},
                        {"childCountError": -25212}, {"pidAfterChildCount": True},
                        *({"childCountSpecial": v} for v in ("undefined", "NaN", "Infinity", "-Infinity"))):
            with self.subTest(options=options):
                result = self.run_fixture(emptyGroup=True, **options)
                self.assertTrue(result["output"]["truncated"])
                action = self.run_fixture(emptyGroup=True, action=True, **options)
                self.assertFalse(action["output"]["performed"])
                self.assertEqual(0, action["presses"])

    def test_unsupported_container_does_not_use_zero_count_to_suppress_read_failure(self):
        result = self.run_fixture(hiddenContainer=True, childCountOverride=0)
        self.assertTrue(result["output"]["truncated"])
        self.assertFalse(any(call.endswith(":children-count") for call in result["calls"]))

    def test_untrusted_or_wrong_root_pid_cannot_read_the_window_tree(self):
        for options in ({"untrusted": True}, {"foreignPid": True}):
            with self.subTest(options=options):
                result = self.run_fixture(**options)
                self.assertTrue(result["output"]["truncated"])
                self.assertFalse(result["output"]["enabled"])
                self.assertNotIn("app:AXWindows", result["reads"])

    def test_unknown_children_error_and_all_budgets_are_partial_never_absence_evidence(self):
        for option in ("missingChildren", "noChildrenValue", "deep", "many", "cycle", "slow", "unknownGeometry", "foreignHit"):
            with self.subTest(option=option):
                result = self.run_fixture(**{option: True})
                self.assertTrue(result["output"]["truncated"])
                self.assertEqual(0, result["presses"])
                with self.assertRaisesRegex(flow.probe.ProbeFailure, "IncompleteNativeTree"):
                    flow.nodes(result["output"])

    def test_explicitly_unsupported_children_on_leaf_is_not_a_read_failure(self):
        result = self.run_fixture(unsupportedLeaf=True)
        self.assertTrue(result["output"]["enabled"])
        self.assertFalse(result["output"]["truncated"])
        self.assertTrue(probe.summarize(result["output"])["capabilityPassed"])

    def test_invisible_windows_never_count_as_visible_controls(self):
        for option in ("hidden", "minimized"):
            with self.subTest(option=option):
                result = self.run_fixture(**{option: True})
                self.assertFalse(probe.summarize(result["output"])["capabilityPassed"])
                self.assertEqual([], flow.nodes(result["output"]))

    def test_offscreen_occluded_zero_size_and_unverified_hit_never_prove_visibility(self):
        for option in ("offscreen", "occluded", "zeroSize", "noHit"):
            with self.subTest(option=option):
                result = self.run_fixture(**{option: True})
                self.assertFalse(result["output"]["truncated"])
                close = next(n for w in result["output"]["windows"] for n in w["nodes"] if n["name"] == "Close")
                self.assertFalse(close["visible"])
                self.assertNotEqual("owned-hit-test", close["visibilityEvidence"])
                self.assertEqual([], flow.matches(result["output"], "Close", "button"))

    def test_owned_descendant_hit_is_accepted_only_after_target_and_window_ancestry_checks(self):
        result = self.run_fixture(action=True, descendantHit=True)
        self.assertTrue(result["output"]["performed"])
        self.assertEqual(1, result["presses"])
        self.assertIn("child-hit:AXParent", result["reads"])

    def test_offscreen_static_text_cannot_satisfy_visible_state(self):
        result = self.run_fixture(offscreenText=True)
        self.assertFalse(result["output"]["truncated"])
        self.assertFalse(flow.has_text(result["output"], "No matching resources"))

    def test_action_uses_complete_fresh_tree_and_exact_control_reference(self):
        result = self.run_fixture(action=True)
        self.assertTrue(result["output"]["performed"])
        self.assertEqual(1, result["presses"])
        self.assertGreaterEqual(result["reads"].count("close:AXTitle"), 2)

    def test_no_action_on_partial_ambiguous_changed_replaced_hidden_or_reused_process(self):
        for option in ("missingChildren", "duplicate", "changedControl", "replacedControl", "hiddenAfterRead", "pidChanged", "slow",
                       "offscreen", "occluded", "zeroSize", "noHit", "foreignHit"):
            with self.subTest(option=option):
                result = self.run_fixture(action=True, **{option: True})
                self.assertFalse(result["output"]["performed"])
                self.assertEqual(0, result["presses"])

    def test_action_error_or_unsupported_operation_has_no_success_fallback(self):
        for options in ({"pressError": True}, {"operation": "set"}, {"operation": "toggle"}):
            with self.subTest(options=options):
                result = self.run_fixture(action=True, **options)
                self.assertFalse(result["output"]["performed"])
                self.assertEqual(0, result["presses"])


class ProviderBoundary(unittest.TestCase):
    @unittest.skipUnless(sys.platform == "darwin", "Pure CFIndex out-parameter ABI regression is macOS only")
    def test_real_cfindex_out_parameter_uses_canonical_string_without_ui_calls(self):
        source = "ObjC.import('Foundation');\n" + (HERE / "macos-cf-values.js").read_text() + r'''
ObjC.bindFunction('calloc',['unsigned char *',['unsigned long','unsigned long']]);
ObjC.bindFunction('free',['void',['void *']]);
const values=[];
for(const text of ['', 'abc']) {
 const out=Ref(),buffer=$.calloc(16,1);
 try {
  const length=Number($.CFStringGetBytes($(text),$.CFRangeMake(0,text.length),$.kCFStringEncodingUTF8,0,false,buffer,16,out));
  values.push({type:typeof out[0],length:length,normalized:cfIndex(out[0],1000)});
 }finally{$.free(buffer);}
}
JSON.stringify(values);
'''
        process = subprocess.run(["/usr/bin/osascript", "-l", "JavaScript", "-"], input=source,
                                 capture_output=True, text=True, timeout=5, check=True)
        self.assertEqual([{"type": "string", "length": n, "normalized": {"kind": "decimal-string", "value": n}}
                          for n in (0, 3)], json.loads(process.stdout))

    @unittest.skipUnless(sys.platform == "darwin", "Pure CF container ABI regression is macOS only")
    def test_real_cf_container_generic_pointer_preserves_value_identity_for_typed_calls(self):
        source = "ObjC.import('ApplicationServices');ObjC.import('Foundation');\n" + (HERE / "macos-cf-values.js").read_text() + r'''
ObjC.bindFunction('calloc',['double *',['unsigned long','unsigned long']]);
ObjC.bindFunction('free',['void',['void *']]);
const values=[];
for(const kind of [1,2]) {
 const buffer=$.calloc(2,8);buffer[0]=12.5;buffer[1]=22.25;
 let original;
 try {original=$.AXValueCreate(kind,buffer);}finally{$.free(buffer);}
 // Native addObject avoids JXA's JS-array container wrapping. Its item is the
 // same underlying AXValue but CFArrayGetValueAtIndex returns a void* Ref.
 const array=$.NSMutableArray.alloc.init;array.addObject(cfValue(original).object);
 const item=$.CFArrayGetValueAtIndex(array,0);
 let rejected=false;
 try {$.AXValueGetType(item);}catch(_){rejected=true;}
 values.push({genericPointerRejected:rejected,same:!!$.CFEqual(cfValue(original).object,cfValue(item).object),
   pair:cfPair(item,kind),kind:Number($.AXValueGetType(cfValue(item).object))});
}
const booleans=$.NSMutableArray.alloc.init;booleans.addObject($(false));booleans.addObject($(true));
JSON.stringify({values:values,booleans:[0,1].map(i=>!!$.CFBooleanGetValue(cfValue($.CFArrayGetValueAtIndex(booleans,i)).object))});
'''
        process = subprocess.run(["/usr/bin/osascript", "-l", "JavaScript", "-"], input=source,
                                 capture_output=True, text=True, timeout=5, check=True)
        self.assertEqual({"values": [{"genericPointerRejected": True, "same": True, "pair": [12.5, 22.25], "kind": k}
                                      for k in (1, 2)], "booleans": [False, True]}, json.loads(process.stdout))

    @unittest.skipUnless(sys.platform == "darwin", "Pure AXValue ABI regression is macOS only")
    def test_real_axvalue_point_and_size_buffers_without_ui_or_trust_calls(self):
        source = "ObjC.import('ApplicationServices');\n" + (HERE / "macos-cf-values.js").read_text() + r'''
ObjC.bindFunction('calloc',['double *',['unsigned long','unsigned long']]);
ObjC.bindFunction('free',['void',['void *']]);
const values=[];
for(const kind of [1,2]) {
 const buffer=$.calloc(2,8);buffer[0]=12.5;buffer[1]=22.25;
 try {values.push(cfPair($.AXValueCreate(kind,buffer),kind));}finally{$.free(buffer);}
}
JSON.stringify(values);
'''
        process = subprocess.run(["/usr/bin/osascript", "-l", "JavaScript", "-"], input=source,
                                 capture_output=True, text=True, timeout=5, check=True)
        self.assertEqual([[12.5, 22.25], [12.5, 22.25]], json.loads(process.stdout))

    def test_direct_reader_keeps_hosted_identity_and_thirty_second_outer_bound(self):
        raw = {"backend": "macos-direct-ax", "readOnly": True, "enabled": True, "truncated": False, "windows": []}
        with patch.object(probe, "hosted") as hosted, patch.object(probe, "mac_identity", return_value=IDENTITY) as identity, \
                patch.object(probe, "owned_identity", return_value={"pid": 42}), \
                patch.object(probe, "bounded_command", return_value=raw) as command:
            probe.native_snapshot(APP, 42)
        hosted.assert_called_once()
        self.assertEqual(2, identity.call_count)
        self.assertGreater(command.call_args.args[2], 0)
        self.assertLessEqual(command.call_args.args[2], 25)
        self.assertIn('"readBudgetMs": 24000', command.call_args.args[1])
        self.assertIn("ownedAX(input,input.readBudgetMs).inspect()", command.call_args.args[1])

    def test_provider_mismatch_never_reaches_native_action(self):
        snapshot = {"backend": "macos-system-events-ax", "enabled": True, "truncated": False, "process": IDENTITY}
        with patch.object(flow.probe, "hosted"), patch.object(flow.probe, "mac_identity", return_value=IDENTITY), \
                patch.object(flow.probe, "bounded_command") as command:
            with self.assertRaisesRegex(flow.probe.ProbeFailure, "NativeActionProviderMismatch"):
                flow.perform(APP, snapshot, {})
        command.assert_not_called()

    def test_direct_action_retains_fifteen_second_bound_and_post_action_process_check(self):
        snapshot = {"backend": "macos-direct-ax", "enabled": True, "truncated": False, "process": IDENTITY,
                    "ownedOSIdentity": {"pid": 42}, "ownedOSIdentityStable": True}
        with patch.object(flow.probe, "hosted"), \
                patch.object(flow.probe, "mac_identity", side_effect=[IDENTITY, dict(IDENTITY, started="changed")]), \
                patch.object(flow.probe, "owned_identity", return_value={"pid": 42}), \
                patch.object(flow.probe, "bounded_command", return_value={"performed": True, "operation": "press"}) as command:
            with self.assertRaisesRegex(flow.probe.ProbeFailure, "ProductProcessChangedDuringProbe"):
                flow.perform(APP, snapshot, {})
        self.assertGreater(command.call_args.args[2], 0)
        self.assertLessEqual(command.call_args.args[2], 10)
        self.assertIn("Math.min(12000,input.readBudgetMs", command.call_args.args[1])

    def test_partial_direct_tree_cannot_enable_flow_and_does_not_fallback(self):
        with tempfile.TemporaryDirectory() as temporary:
            apps = {role: {"role": role, "rid": "osx-x64", "results": Path(temporary) / role} for role in ("client", "unified")}
            for app in apps.values(): app["results"].mkdir()
            report = {"requestedFlow": "empty-library", "macosObserver": "direct-ax"}
            with patch.object(runner.lifecycle, "install_app", return_value={}), \
                    patch.object(runner.lifecycle, "observe_app", return_value={"processIds": [42]}), \
                    patch.object(runner.probe, "capture", side_effect=[{"capabilityPassed": True}, {"capabilityPassed": True,"completeTreePassed": False}]*2) as capture, \
                    patch.object(runner.lifecycle, "require_same_process"), \
                    patch.object(runner.direct_ax, "capture", return_value={"available": True}), patch.object(runner, "load") as load_flow:
                with self.assertRaisesRegex(AssertionError, "complete-tree gate"):
                    runner.exercise(apps, report)
            self.assertEqual(4, capture.call_count)
            self.assertTrue(capture.call_args.kwargs["require_complete"])
            self.assertFalse(report["directAXTrees"]["client"]["completeTreePassed"])
            self.assertNotIn("nativeBackend", apps["client"])
            load_flow.assert_not_called()

    def test_only_two_complete_independent_trees_select_matching_provider_for_flow(self):
        with tempfile.TemporaryDirectory() as temporary:
            apps = {role: {"role": role, "rid": "osx-x64", "results": Path(temporary) / role} for role in ("client", "unified")}
            for app in apps.values(): app["results"].mkdir()
            report = {"requestedFlow": "empty-library", "macosObserver": "direct-ax", "mainFlowPassed": False}
            observed_apps = []

            def native_flow(app, pid, destination):
                observed_apps.append(dict(app))
                self.assertEqual(42, pid)
                self.assertTrue(all(report["directAXTrees"][role]["completeTreePassed"] for role in apps))
                return {"emptyLibraryFlowPassed": True, "mainFlowPassed": False, "nativeBackend": app["nativeBackend"]}

            with patch.object(runner.lifecycle, "install_app", return_value={}), \
                    patch.object(runner.lifecycle, "observe_app", return_value={"processIds": [42]}), \
                    patch.object(runner.lifecycle, "require_same_process"), \
                    patch.object(runner.probe, "capture", side_effect=[{"capabilityPassed": False}, {"capabilityPassed": True,"completeTreePassed": True}]*2), \
                    patch.object(runner.direct_ax, "capture", return_value={"available": True}), \
                    patch.object(runner, "load", return_value=SimpleNamespace(run_empty_library=native_flow, arm=lambda app: {"armed": True})):
                runner.exercise(apps, report)
            self.assertEqual(1, len(observed_apps))
            self.assertEqual("macos-direct-ax", observed_apps[0]["nativeBackend"])
            self.assertTrue(report["emptyLibraryFlowPassed"])
            self.assertFalse(report["mainFlowPassed"])

    def test_diagnostic_unknown_strings_never_enter_report(self):
        safe = probe.ax_diagnostic({"code": "SECRET", "attribute": "SECRET", "operation": "SECRET", "axError": "SECRET",
                                    "countKind": "SECRET", "countValue": "SECRET", "raw": "SECRET"})
        self.assertNotIn("SECRET", json.dumps(safe))


if __name__ == "__main__":
    unittest.main()
