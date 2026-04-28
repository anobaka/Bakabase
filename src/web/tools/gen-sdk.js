import { spawnSync } from "node:child_process";
import * as path from "node:path";
import * as fs from "node:fs";
import { fileURLToPath } from "node:url";
import { generateApi } from "swagger-typescript-api";

// ---------- paths ----------
const __dirname = path.dirname(fileURLToPath(import.meta.url));
const webDir = path.resolve(__dirname, "..");
const cliProj = path.resolve(webDir, "../apps/Bakabase.Cli/Bakabase.Cli.csproj");
const cacheDir = path.resolve(webDir, ".sdk-cache");
const sdkDir = path.resolve(webDir, "src/sdk");
const swaggerPath = path.resolve(cacheDir, "swagger.json");
const constantsPath = path.resolve(sdkDir, "constants.ts");
const apiPath = path.resolve(sdkDir, "Api.ts");
const bapi2Path = path.resolve(sdkDir, "BApi2.d.ts");

fs.mkdirSync(cacheDir, { recursive: true });

function run(cmd, args, opts = {}) {
  console.log(`$ ${cmd} ${args.join(" ")}`);
  const r = spawnSync(cmd, args, { stdio: "inherit", ...opts });
  if (r.status !== 0) process.exit(r.status ?? 1);
}

// ---------- Api.ts post-processing ----------
const IMPORTS = `import { buildLogger, extractErrorMessage } from "@/components/utils.tsx";
import { toast } from "@/components/bakaui";
`;

const LOG = `
const log = buildLogger("BApi");
`;

const BASE_RESPONSE_TYPE = `type BaseResponse = {
  code: number;
  message: string;
};
`;

const SHOW_ERROR_TOAST = `  showErrorToast?: (response: BaseResponse) => boolean;`;

function buildUrlBuilders(content) {
  const methodRegex =
    /(\w+):\s*\(([^)]*)\)\s*=>\s*this\.request[^(]*\([^{]*\{\s*path:\s*`([^`]+)`[^}]*method:\s*"(\w+)"[^}]*\}/gs;

  const urlBuilders = [];
  let match;

  while ((match = methodRegex.exec(content)) !== null) {
    const [, methodName, params, pathTemplate] = match;

    let paramList = [];
    let depth = 0;
    let currentParam = "";

    for (let i = 0; i < params.length; i++) {
      const char = params[i];
      if (char === "<" || char === "{") depth++;
      if (char === ">" || char === "}") depth--;

      if (char === "," && depth === 0) {
        if (currentParam.trim()) paramList.push(currentParam.trim());
        currentParam = "";
      } else {
        currentParam += char;
      }
    }
    if (currentParam.trim()) paramList.push(currentParam.trim());

    paramList = paramList
      .filter((p) => !p.includes("params:") && !p.includes("RequestParams"))
      .map((p) => {
        const colonIndex = p.indexOf(":");
        if (colonIndex === -1) return null;

        let name = p.substring(0, colonIndex).trim();
        let type = p.substring(colonIndex + 1).trim();

        const equalsIndex = type.indexOf("=");
        if (equalsIndex !== -1) type = type.substring(0, equalsIndex).trim();

        const isOptional = name.endsWith("?") || type.startsWith("?");
        name = name.replace("?", "");
        type = type.replace(/^\?/, "").trim();

        return { name, type, isOptional };
      })
      .filter(Boolean);

    const pathParams = [...pathTemplate.matchAll(/\$\{([^}]+)\}/g)].map((m) => m[1]);
    const urlBuilderName = `${methodName}Url`;
    const pathParamsList = paramList.filter((p) =>
      pathParams.some((pp) => pp === p.name || pp.includes(p.name)),
    );
    const queryParamObj = paramList.find((p) => p.name === "query" || p.name.includes("query"));
    const hasQueryParams = !!queryParamObj;

    const urlBuilderParams = [...pathParamsList];
    if (hasQueryParams) urlBuilderParams.push(queryParamObj);

    const paramString = urlBuilderParams
      .map((p) => `${p.name}${p.isOptional ? "?" : ""}: ${p.type}`)
      .join(", ");

    let code = `
    /**
     * @description Build URL for ${methodName}
     * @name ${urlBuilderName}
     */
    ${urlBuilderName}: (${paramString}) => {
      const baseUrl = this.baseUrl || "";
      let path = \`${pathTemplate}\`;
      `;

    if (hasQueryParams) {
      code += `
      // Build query string
      if (${queryParamObj.name}) {
        const queryString = Object.keys(${queryParamObj.name})
          .filter(key => ${queryParamObj.name}[key] !== undefined && ${queryParamObj.name}[key] !== null)
          .map(key => \`\${encodeURIComponent(key)}=\${encodeURIComponent(String(${queryParamObj.name}[key]))}\`)
          .join("&");

        return baseUrl + path + (queryString ? \`?\${queryString}\` : "");
      }
      `;
    }

    code += `
      return baseUrl + path;
    },`;

    urlBuilders.push({ methodName, code });
  }

  return urlBuilders;
}

function injectUrlBuilders(content) {
  const urlBuilders = buildUrlBuilders(content);

  if (urlBuilders.length === 0) {
    console.warn("No API methods found to generate URL builders");
    return content;
  }

  let modifiedContent = content;
  let offset = 0;

  for (const builder of urlBuilders) {
    const methodPattern = new RegExp(
      `(${builder.methodName}:\\s*\\([^)]*\\)\\s*=>\\s*this\\.request[^(]*\\([^{]*\\{[^}]*\\}\\s*\\),)`,
      "s",
    );

    const match = modifiedContent.slice(offset).match(methodPattern);
    if (match) {
      const matchEnd = offset + match.index + match[0].length;
      modifiedContent =
        modifiedContent.slice(0, matchEnd) + "\n" + builder.code + modifiedContent.slice(matchEnd);
      offset = matchEnd + builder.code.length + 1;
    }
  }

  console.log(`Generated ${urlBuilders.length} URL builder methods`);
  return modifiedContent;
}

async function buildApiTs() {
  await generateApi({
    input: swaggerPath,
    name: "Api.ts",
    output: sdkDir,
    unwrapResponseData: true,
    generateUnionEnums: true,
    modular: false,
  });

  if (!fs.existsSync(apiPath)) return;

  let content = fs.readFileSync(apiPath, "utf-8");

  content = IMPORTS + LOG + content;

  content = content.replace(
    'export type ResponseFormat = keyof Omit<Body, "body" | "bodyUsed">;',
    'export type ResponseFormat = keyof Omit<Body, "body" | "bodyUsed">;\n\n' + BASE_RESPONSE_TYPE,
  );

  content = content.replace(
    "cancelToken?: CancelToken;",
    "cancelToken?: CancelToken;\n\n" + SHOW_ERROR_TOAST,
  );

  content = content.replace(
    "public request = async <T = any, E = any>({",
    "public request = async <T = any, E = any>(fullRequestParams: FullRequestParams): Promise<T> => {\n    const {",
  );

  content = content.replace("}: FullRequestParams): Promise<T> => {", "  } = fullRequestParams;");

  const errorTitleTpl = "`${params.method} ${params.path} failed`";
  const apiErrorTitleTpl = "`[${response.code}]${params.method} ${params.path}`";
  content = content.replace(
    "if (!response.ok) throw data;\n      return data.data;\n    });\n  };",
    `
      if (!response.ok) {
        this.processResponseError(data, fullRequestParams);
        throw data;
      }
      return this.processResponseData(data.data as BaseResponse, fullRequestParams);
    }).catch((error) => {
      this.processResponseError(error, fullRequestParams);
      throw error;
    });
  };

  protected processResponseError = (error: any, params: FullRequestParams) => {
    const title = ${errorTitleTpl};
    const description = extractErrorMessage(error);

    toast.danger({
      title,
      description,
    });
  };

  protected processResponseData = <T = any>(response: BaseResponse, params: FullRequestParams): T => {
    // Check if the response has a code property (API response structure)
    if (response && typeof response === "object" && "code" in response) {
      log('[response]', params.path, response)
      switch (response.code) {
        case 0:
          break;
        default:
          const showErrorToast = params.showErrorToast || ((error: BaseResponse) => error.code >= 400 || error.code < 200);
          if (showErrorToast(response)) {
            const title = ${apiErrorTitleTpl};

            toast.danger({
              title,
              description: response.message,
            });
          }
      }
    }

    return response as T;
  };

        `,
  );

  content = injectUrlBuilders(content);

  fs.writeFileSync(apiPath, content, "utf-8");
}

// ---------- pipeline ----------
run("dotnet", ["run", "--project", cliProj, "--", "swagger", swaggerPath]);
run("dotnet", ["run", "--project", cliProj, "--", "constants", constantsPath]);

console.log(`Building Api.ts from ${swaggerPath}`);
await buildApiTs();

run("npx", ["openapi-typescript", swaggerPath, "-o", bapi2Path], { cwd: webDir });

console.log("SDK generation complete.");
