/// The server's StandardValue ListString wire format: items joined by `,`,
/// with `\` escaping both the separator and itself. Mirrors
/// `StandardValueExtensions.SerializeAsStandardValue` on the C# side — the
/// search filter DSL takes its `dbValue` in exactly this encoding.
String serializeListString(List<String> items) {
  return items
      .map((item) => item.replaceAll(r'\', r'\\').replaceAll(',', r'\,'))
      .join(',');
}
