const Handlebars = require("handlebars");
const fs = require("fs");
const path = require("path");
const beautify = require("js-beautify").js_beautify;
const apis = fs.readFileSync(
  path.join(__dirname, "./templates/api.hbs"),
  "utf-8"
);
const methods = fs.readFileSync(
  path.join(__dirname, "./templates/methods.hbs"),
  "utf-8"
);
const method = fs.readFileSync(
  path.join(__dirname, "./templates/method.hbs"),
  "utf-8"
);
const apiWrapper = fs.readFileSync(
  path.join(__dirname, "./templates/api-wrapper.hbs"),
  "utf-8"
);
Handlebars.registerPartial("methods", methods);
Handlebars.registerPartial("method", method);
Handlebars.registerHelper("toLowerCase", function(word) {
  return word.toLowerCase();
});
Handlebars.registerHelper("brackets", function(word) {
  return `{${word}}`;
});
module.exports = function(data) {
  const targetFiles = {
    apis: {
      template: apis,
      override: true
    },
    "api-wrapper": {
      template: apiWrapper,
      override: false
    },
  };
  const outputs = {};
  Object.keys(targetFiles).map(filename => {
    const genOptions = targetFiles[filename];
    const {template, ...otherOptions} = genOptions;
    let output = Handlebars.compile(genOptions.template)(data);
    output = beautify(output, { indent_size: 2, max_preserve_newlines: -1 });
    outputs[filename] = {
      data: output,
      ...otherOptions
    };
  });
  return outputs;
};
