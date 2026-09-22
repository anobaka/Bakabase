// Pure value conversion: no applications, AXUIElement calls, trust reads or UI.
// JXA passes an opaque Ref to CFTypeRef parameters as a bridged JS wrapper;
// convert the referenced CF object first, before checking its actual type.
function cfValue(value) {
  const wasRef = value instanceof Ref;
  const rawTypeId = Number($.CFGetTypeID(value));
  const object = wasRef ? ObjC.castRefToObject(value) : value;
  return {object:object, wasRef:wasRef, rawTypeId:rawTypeId,
    typeId:Number($.CFGetTypeID(object))};
}

// JXA can expose a CFIndex* out-parameter as a decimal string. Accept only its
// canonical, bounded integer form;
// null, booleans, whitespace and other coercible values never become zero.
function cfIndex(value,maximum) {
  let kind=value===null?'null':typeof value,number=null;
  if(kind==='number') number=value;
  else if(kind==='string') {
    if(/^(0|[1-9][0-9]{0,3})$/.test(value)) {kind='decimal-string';number=Number(value);}
    else kind='rejected-string';
  } else if(!['null','undefined','boolean'].includes(kind)) kind='other';
  return {kind:kind,value:Number.isInteger(maximum)&&maximum>=0&&maximum<=1000&&
    Number.isInteger(number)&&number>=0&&number<=maximum?number:null};
}

// AXValue is a value container, not an AXUIElement. The caller binds calloc/free
// and imports ApplicationServices; only a private 16-byte CGPoint/CGSize buffer
// is allocated here. Both supported macOS RIDs have 64-bit CGFloat.
function cfPair(value,type) {
  const converted=cfValue(value);
  if(converted.typeId!==Number($.AXValueGetTypeID())) throw Error('InvalidAXValueType');
  // A CFArray/attribute out-parameter has a generic pointer type. Passing that
  // Ref to an AXValueRef parameter fails JXA's type check; pass the validated
  // underlying CF object, preserving its identity, rather than the wrapper.
  const object=converted.object;
  if(Number($.AXValueGetType(object))!==type) throw Error('InvalidAXValueKind');
  const buffer=$.calloc(2,8);
  try {
    if(!$.AXValueGetValue(object,type,buffer)) throw Error('InvalidAXValueData');
    const values=[Number(buffer[0]),Number(buffer[1])];
    if(values.some(n=>!Number.isFinite(n)||Math.abs(n)>100000)) throw Error('InvalidAXValueBounds');
    return values;
  } finally {$.free(buffer);}
}
