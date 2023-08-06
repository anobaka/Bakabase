import axios from 'axios';
import qs from 'qs';
import {
  Message
} from '@alifd/next';
import {
  buildLoginRedirectPath
} from '@anobaka/bootstrap-fe/helpers/router';
import serverConfig from '../serverConfig';
import appConfig from '../appConfig';
export default function request(
  method,
  url,
  body,
  queryParameters,
  form,
  config,
  responseConverter
) {
  if (serverConfig.beforeRequesting) {
    serverConfig.beforeRequesting(method, url, body, queryParameters, form, config, responseConverter);
  }
  if (url.indexOf('://') < 0) {
    url = serverConfig.apiEndpoint + url;
  }
  method = method.toLowerCase();
  const returnObj = {};
  const options = {
    url,
    method,
    params: {
      ...queryParameters,
      _t: new Date().getTime(),
    },
  };
  if (body) {
    options.data = body;
  } else if (method !== 'get') {
    options.data = qs.stringify(form);
  }
  // 默认的会导致array形式的query param在.net侧无法解析
  options.paramsSerializer = function(params) {
    return qs.stringify(params);
  };
  options.maxRedirects = 0;
  options.withCredentials = true;
  returnObj.options = options;
  returnObj.invoke = function(callback, responseHandler) {
    const promise = new Promise((resolve) => {
      axios
        .request(options)
        .then((res) => {
          if (res.status == 200) {
            if (responseConverter) {
              res.data = responseConverter(res.data);
            }
            switch (res.data.code) {
              case 401:
                const loginPath = appConfig.getLoginPath();
                // 只允许一次请求跳转到session
                if (location.hash.indexOf(loginPath) < 0) {
                  location.hash = buildLoginRedirectPath(loginPath);
                }
                break;
              case 0:
                resolve(res.data);
                break;
              default:
                Message.error(res.data.message);
                resolve(res.data);
                break;
            }
          } else {
            Message.error('请求异常，请稍后再试');
          }
        })
        .catch((error) => {
          Message.error(`请求异常，请稍后再试。${error}`);
        });
    });
    if (callback) {
      return promise.then(callback);
    }
    return promise;
  };
  return returnObj;
}