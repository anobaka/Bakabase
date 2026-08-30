import 'package:bakabase_mobile/core/list_string.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('joins with commas', () {
    expect(serializeListString(['1', '2']), '1,2');
  });

  test('a single item is itself', () {
    expect(serializeListString(['5']), '5');
  });

  test('escapes the separator and the escape char, in that order', () {
    // Mirrors StringExtensions.Join on the server: escape '\' first, then ','.
    expect(serializeListString([r'a,b', r'c\d']), r'a\,b,c\\d');
  });
}
