import path from 'path';
import http from 'http';
import https from 'https';
import fs from 'fs';
import { fileURLToPath } from 'url';

// Get __dirname equivalent in ES modules
const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

// This method will be refactoring.
function generateSdk(swaggerJsonUrl, outputDir) {
  console.log(`Generating SDK: ${swaggerJsonUrl}`);
  const requestInvoker = swaggerJsonUrl.startsWith('http://') ? http : https;
  const constantUrl = `${new URL(swaggerJsonUrl).origin}/api/constant`;
  requestInvoker.get(constantUrl, (response) => {
    if (response.statusCode == 200) {
      const jsonFilename = path.join(outputDir, './constants.ts');
      const file = fs.createWriteStream(jsonFilename);
      const stream = response.pipe(file);
      stream.on('finish', () => {
        console.log(`Constants generated: ${constantUrl} -> ${jsonFilename}`);
      });
    }
  });
}

let host = 'http://127.0.0.1:8080';
generateSdk(`${host}/internal-doc/swagger/v1/swagger.json`, __dirname);
