const parse = require('./parse.js')
const codegen = require('./codegen.js')
module.exports = function (opt) {
  let data = parse(opt)
  let outputs = codegen(data)
  return outputs
}
