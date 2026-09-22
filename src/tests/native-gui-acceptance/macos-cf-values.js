// Pure value conversion: no applications, AX calls, permission reads or UI.
// JXA passes an opaque Ref to CFTypeRef parameters as a bridged JS wrapper;
// convert the referenced CF object first, before checking its actual type.
function cfValue(value) {
  const wasRef = value instanceof Ref;
  const rawTypeId = Number($.CFGetTypeID(value));
  const object = wasRef ? ObjC.castRefToObject(value) : value;
  return {object:object, wasRef:wasRef, rawTypeId:rawTypeId,
    typeId:Number($.CFGetTypeID(object))};
}
