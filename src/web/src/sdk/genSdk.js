import * as path from "node:path";
import * as process from "node:process";
import * as fs from "node:fs";
import { generateApi } from "swagger-typescript-api";

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

const SHOW_ERROR_TOAST = `  showErrorToast?: (response: BaseResponse) => boolean;`

const bt = String.fromCharCode(96);       // 等价于 `
const dollar = "$";
const curlyOpen = "{";
const curlyClose = "}";

/**
 * Generate URL builder methods for each API endpoint
 */
function generateUrlBuilders(content) {
  // Regular expression to match API method definitions
  // Matches: methodName: (param1, param2, params: RequestParams = {}) => this.request<...>({ path: `/path`, method: "GET", ...})
  const methodRegex = /(\w+):\s*\(([^)]*)\)\s*=>\s*this\.request[^(]*\([^{]*\{\s*path:\s*`([^`]+)`[^}]*method:\s*"(\w+)"[^}]*\}/gs;
  
  const urlBuilders = [];
  let match;
  
  while ((match = methodRegex.exec(content)) !== null) {
    const [fullMatch, methodName, params, pathTemplate, httpMethod] = match;
    
    // Parse parameters - split by comma but handle nested generics
    let paramList = [];
    let depth = 0;
    let currentParam = '';
    
    for (let i = 0; i < params.length; i++) {
      const char = params[i];
      if (char === '<' || char === '{') depth++;
      if (char === '>' || char === '}') depth--;
      
      if (char === ',' && depth === 0) {
        if (currentParam.trim()) paramList.push(currentParam.trim());
        currentParam = '';
      } else {
        currentParam += char;
      }
    }
    if (currentParam.trim()) paramList.push(currentParam.trim());
    
    // Filter out the params parameter and parse the rest
    paramList = paramList
      .filter(p => !p.includes('params:') && !p.includes('RequestParams'))
      .map(p => {
        // Handle parameters with optional types, defaults, etc.
        const colonIndex = p.indexOf(':');
        if (colonIndex === -1) return null;
        
        let name = p.substring(0, colonIndex).trim();
        let type = p.substring(colonIndex + 1).trim();
        
        // Remove default values
        const equalsIndex = type.indexOf('=');
        if (equalsIndex !== -1) {
          type = type.substring(0, equalsIndex).trim();
        }
        
        // Check if optional
        const isOptional = name.endsWith('?') || type.startsWith('?');
        name = name.replace('?', '');
        type = type.replace(/^\?/, '').trim();
        
        return { name, type, isOptional };
      })
      .filter(p => p !== null);
    
    // Extract path parameters from template
    const pathParams = [...pathTemplate.matchAll(/\$\{([^}]+)\}/g)].map(m => m[1]);
    
    // Generate URL builder method
    const urlBuilderName = `${methodName}Url`;
    
    // Parameters needed: path params + query object if it exists
    const pathParamsList = paramList.filter(p => 
      pathParams.some(pp => pp === p.name || pp.includes(p.name))
    );
    
    const queryParamObj = paramList.find(p => 
      p.name === 'query' || p.name.includes('query')
    );
    
    const hasQueryParams = !!queryParamObj;
    
    // Build parameter list for URL builder
    let urlBuilderParams = [...pathParamsList];
    if (hasQueryParams) {
      urlBuilderParams.push(queryParamObj);
    }
    
    const paramString = urlBuilderParams.map(p => {
      const optional = p.isOptional ? '?' : '';
      return `${p.name}${optional}: ${p.type}`;
    }).join(', ');
    
    let urlBuilderCode = `
    /**
     * @description Build URL for ${methodName}
     * @name ${urlBuilderName}
     */
    ${urlBuilderName}: (${paramString}) => {
      const baseUrl = this.baseUrl || "";
      let path = \`${pathTemplate}\`;
      `;
    
    if (hasQueryParams) {
      urlBuilderCode += `
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
    
    urlBuilderCode += `
      return baseUrl + path;
    },`;
    
    urlBuilders.push({
      methodName,
      urlBuilderName,
      code: urlBuilderCode
    });
  }
  
  return urlBuilders;
}

/**
 * Inject URL builders into the API class
 */
function injectUrlBuilders(content) {
  const urlBuilders = generateUrlBuilders(content);
  
  if (urlBuilders.length === 0) {
    console.warn('No API methods found to generate URL builders');
    return content;
  }
  
  // For each URL builder, find its corresponding method and inject the URL builder after it
  let modifiedContent = content;
  let offset = 0; // Track cumulative offset from insertions
  
  for (const builder of urlBuilders) {
    // Find the method definition in the content
    // Pattern: methodName: (...) => this.request<...>({...}),
    const methodPattern = new RegExp(
      `(${builder.methodName}:\\s*\\([^)]*\\)\\s*=>\\s*this\\.request[^(]*\\([^{]*\\{[^}]*\\}\\s*\\),)`,
      's'
    );
    
    const match = modifiedContent.slice(offset).match(methodPattern);
    if (match) {
      const matchEnd = offset + match.index + match[0].length;
      
      // Insert the URL builder right after the method
      modifiedContent = 
        modifiedContent.slice(0, matchEnd) + 
        '\n' + builder.code +
        modifiedContent.slice(matchEnd);
      
      // Update offset to account for the insertion
      offset = matchEnd + builder.code.length + 1;
    }
  }
  
  console.log(`Generated ${urlBuilders.length} URL builder methods`);
  
  return modifiedContent;
}

async function generateSdk() {
  try {

    const outputPath = path.resolve(process.cwd(), "./src/sdk");
    const outputFile = path.resolve(outputPath, "Api.ts");

    console.log(`Generating SDK...${outputPath}`);

    await generateApi({
      url: "http://127.0.0.1:8080/internal-doc/swagger/v1/swagger.json",
      name: "Api.ts",
      output: outputPath,
      unwrapResponseData: true,
      generateUnionEnums: true,
      modular: false,
    });

    // 在生成的文件第一行插入导入语句
    const apiFilePath = outputFile;
    if (fs.existsSync(apiFilePath)) {
      let content = fs.readFileSync(apiFilePath, 'utf-8');

      // 在文件开头添加导入语句
      content = IMPORTS + LOG + content;

      content = content.replace(
        'export type ResponseFormat = keyof Omit<Body, "body" | "bodyUsed">;',
        'export type ResponseFormat = keyof Omit<Body, "body" | "bodyUsed">;\n\n' + BASE_RESPONSE_TYPE
      );

      content = content.replace(
        'cancelToken?: CancelToken;',
        'cancelToken?: CancelToken;\n\n' + SHOW_ERROR_TOAST
      );

      content = content.replace(
        'public request = async <T = any, E = any>({',
        'public request = async <T = any, E = any>(fullRequestParams: FullRequestParams): Promise<T> => {\n    const {'
      )

      content = content.replace(
        '}: FullRequestParams): Promise<T> => {',
        '  } = fullRequestParams;'
      )

      const errorTitleTpl = '`${params.method} ${params.path} failed`'
      const apiErrorTitleTpl = '`[${response.code}]${params.method} ${params.path}`'
      content = content.replace(
        'if (!response.ok) throw data;\n      return data.data;\n    });\n  };',
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

        `

      )

      // Inject URL builder methods
      content = injectUrlBuilders(content);

      fs.writeFileSync(apiFilePath, content, 'utf-8');
      console.log("SDK generated successfully!");
    }

  } catch (error) {
    console.error(error);
    process.exit(1);
  }
}

generateSdk();
