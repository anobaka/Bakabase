/* eslint-disable */
let domain = ''
export const getDomain = () => {
  return domain
}
export const setDomain = ($domain) => {
  domain = $domain
}
import request from './api-wrapper';
/*==========================================================
 *                    
 ==========================================================*/
/**
 * 
 * request: GetAlias
 * url: GetAliasURL
 * method: GetAlias_TYPE
 * raw_url: GetAlias_RAW_URL
 * @param id - 
 */
export const GetAlias = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/alias/{id}'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetAlias_RAW_URL = function() {
  return '/alias/{id}'
}
export const GetAlias_TYPE = function() {
  return 'get'
}
export const GetAliasURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/alias/{id}'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: UpdateAlias
 * url: UpdateAliasURL
 * method: UpdateAlias_TYPE
 * raw_url: UpdateAlias_RAW_URL
 * @param id - 
 * @param model - 
 */
export const UpdateAlias = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/alias/{id}'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('put', domain + path, body, queryParameters, form, config)
}
export const UpdateAlias_RAW_URL = function() {
  return '/alias/{id}'
}
export const UpdateAlias_TYPE = function() {
  return 'put'
}
export const UpdateAliasURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/alias/{id}'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: RemoveAlias
 * url: RemoveAliasURL
 * method: RemoveAlias_TYPE
 * raw_url: RemoveAlias_RAW_URL
 * @param id - 
 */
export const RemoveAlias = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/alias/{id}'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('delete', domain + path, body, queryParameters, form, config)
}
export const RemoveAlias_RAW_URL = function() {
  return '/alias/{id}'
}
export const RemoveAlias_TYPE = function() {
  return 'delete'
}
export const RemoveAliasURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/alias/{id}'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: SearchAliases
 * url: SearchAliasesURL
 * method: SearchAliases_TYPE
 * raw_url: SearchAliases_RAW_URL
 * @param names - 
 * @param name - 
 * @param exactly - 
 * @param additionalItems - 
 * @param pageIndex - 
 * @param pageSize - 
 * @param skipCount - 
 */
export const SearchAliases = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/alias'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['names'] !== undefined) {
    queryParameters['names'] = parameters['names']
  }
  if (parameters['name'] !== undefined) {
    queryParameters['name'] = parameters['name']
  }
  if (parameters['exactly'] !== undefined) {
    queryParameters['exactly'] = parameters['exactly']
  }
  if (parameters['additionalItems'] !== undefined) {
    queryParameters['additionalItems'] = parameters['additionalItems']
  }
  if (parameters['pageIndex'] !== undefined) {
    queryParameters['pageIndex'] = parameters['pageIndex']
  }
  if (parameters['pageSize'] !== undefined) {
    queryParameters['pageSize'] = parameters['pageSize']
  }
  if (parameters['skipCount'] !== undefined) {
    queryParameters['skipCount'] = parameters['skipCount']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const SearchAliases_RAW_URL = function() {
  return '/alias'
}
export const SearchAliases_TYPE = function() {
  return 'get'
}
export const SearchAliasesURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/alias'
  if (parameters['names'] !== undefined) {
    queryParameters['names'] = parameters['names']
  }
  if (parameters['name'] !== undefined) {
    queryParameters['name'] = parameters['name']
  }
  if (parameters['exactly'] !== undefined) {
    queryParameters['exactly'] = parameters['exactly']
  }
  if (parameters['additionalItems'] !== undefined) {
    queryParameters['additionalItems'] = parameters['additionalItems']
  }
  if (parameters['pageIndex'] !== undefined) {
    queryParameters['pageIndex'] = parameters['pageIndex']
  }
  if (parameters['pageSize'] !== undefined) {
    queryParameters['pageSize'] = parameters['pageSize']
  }
  if (parameters['skipCount'] !== undefined) {
    queryParameters['skipCount'] = parameters['skipCount']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: CreateAlias
 * url: CreateAliasURL
 * method: CreateAlias_TYPE
 * raw_url: CreateAlias_RAW_URL
 * @param model - 
 */
export const CreateAlias = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/alias'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('post', domain + path, body, queryParameters, form, config)
}
export const CreateAlias_RAW_URL = function() {
  return '/alias'
}
export const CreateAlias_TYPE = function() {
  return 'post'
}
export const CreateAliasURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/alias'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: ExportAliases
 * url: ExportAliasesURL
 * method: ExportAliases_TYPE
 * raw_url: ExportAliases_RAW_URL
 */
export const ExportAliases = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/alias/export'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('post', domain + path, body, queryParameters, form, config)
}
export const ExportAliases_RAW_URL = function() {
  return '/alias/export'
}
export const ExportAliases_TYPE = function() {
  return 'post'
}
export const ExportAliasesURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/alias/export'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: ImportAliases
 * url: ImportAliasesURL
 * method: ImportAliases_TYPE
 * raw_url: ImportAliases_RAW_URL
 * @param model - 
 */
export const ImportAliases = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/alias/import'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('post', domain + path, body, queryParameters, form, config)
}
export const ImportAliases_RAW_URL = function() {
  return '/alias/import'
}
export const ImportAliases_TYPE = function() {
  return 'post'
}
export const ImportAliasesURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/alias/import'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: RemoveAliasGroup
 * url: RemoveAliasGroupURL
 * method: RemoveAliasGroup_TYPE
 * raw_url: RemoveAliasGroup_RAW_URL
 * @param id - 
 */
export const RemoveAliasGroup = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/alias-group/{id}'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('delete', domain + path, body, queryParameters, form, config)
}
export const RemoveAliasGroup_RAW_URL = function() {
  return '/alias-group/{id}'
}
export const RemoveAliasGroup_TYPE = function() {
  return 'delete'
}
export const RemoveAliasGroupURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/alias-group/{id}'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: MergeAliasGroup
 * url: MergeAliasGroupURL
 * method: MergeAliasGroup_TYPE
 * raw_url: MergeAliasGroup_RAW_URL
 * @param id - 
 * @param model - 
 */
export const MergeAliasGroup = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/alias-group/{id}'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('put', domain + path, body, queryParameters, form, config)
}
export const MergeAliasGroup_RAW_URL = function() {
  return '/alias-group/{id}'
}
export const MergeAliasGroup_TYPE = function() {
  return 'put'
}
export const MergeAliasGroupURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/alias-group/{id}'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: CheckAppInitialized
 * url: CheckAppInitializedURL
 * method: CheckAppInitialized_TYPE
 * raw_url: CheckAppInitialized_RAW_URL
 */
export const CheckAppInitialized = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/app/initialized'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const CheckAppInitialized_RAW_URL = function() {
  return '/app/initialized'
}
export const CheckAppInitialized_TYPE = function() {
  return 'get'
}
export const CheckAppInitializedURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/app/initialized'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetAppInfo
 * url: GetAppInfoURL
 * method: GetAppInfo_TYPE
 * raw_url: GetAppInfo_RAW_URL
 */
export const GetAppInfo = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/app/info'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetAppInfo_RAW_URL = function() {
  return '/app/info'
}
export const GetAppInfo_TYPE = function() {
  return 'get'
}
export const GetAppInfoURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/app/info'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: AcceptTerms
 * url: AcceptTermsURL
 * method: AcceptTerms_TYPE
 * raw_url: AcceptTerms_RAW_URL
 */
export const AcceptTerms = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/app/terms'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('post', domain + path, body, queryParameters, form, config)
}
export const AcceptTerms_RAW_URL = function() {
  return '/app/terms'
}
export const AcceptTerms_TYPE = function() {
  return 'post'
}
export const AcceptTermsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/app/terms'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: MoveCoreData
 * url: MoveCoreDataURL
 * method: MoveCoreData_TYPE
 * raw_url: MoveCoreData_RAW_URL
 * @param model - 
 */
export const MoveCoreData = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/app/data-path'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('put', domain + path, body, queryParameters, form, config)
}
export const MoveCoreData_RAW_URL = function() {
  return '/app/data-path'
}
export const MoveCoreData_TYPE = function() {
  return 'put'
}
export const MoveCoreDataURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/app/data-path'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetAllBackgroundTasks
 * url: GetAllBackgroundTasksURL
 * method: GetAllBackgroundTasks_TYPE
 * raw_url: GetAllBackgroundTasks_RAW_URL
 */
export const GetAllBackgroundTasks = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/background-task'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetAllBackgroundTasks_RAW_URL = function() {
  return '/background-task'
}
export const GetAllBackgroundTasks_TYPE = function() {
  return 'get'
}
export const GetAllBackgroundTasksURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/background-task'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: ClearInactiveBackgroundTasks
 * url: ClearInactiveBackgroundTasksURL
 * method: ClearInactiveBackgroundTasks_TYPE
 * raw_url: ClearInactiveBackgroundTasks_RAW_URL
 */
export const ClearInactiveBackgroundTasks = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/background-task'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('delete', domain + path, body, queryParameters, form, config)
}
export const ClearInactiveBackgroundTasks_RAW_URL = function() {
  return '/background-task'
}
export const ClearInactiveBackgroundTasks_TYPE = function() {
  return 'delete'
}
export const ClearInactiveBackgroundTasksURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/background-task'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetBackgroundTaskByName
 * url: GetBackgroundTaskByNameURL
 * method: GetBackgroundTaskByName_TYPE
 * raw_url: GetBackgroundTaskByName_RAW_URL
 * @param name - 
 */
export const GetBackgroundTaskByName = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/background-task/by-name'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['name'] !== undefined) {
    queryParameters['name'] = parameters['name']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetBackgroundTaskByName_RAW_URL = function() {
  return '/background-task/by-name'
}
export const GetBackgroundTaskByName_TYPE = function() {
  return 'get'
}
export const GetBackgroundTaskByNameURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/background-task/by-name'
  if (parameters['name'] !== undefined) {
    queryParameters['name'] = parameters['name']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: StopBackgroundTask
 * url: StopBackgroundTaskURL
 * method: StopBackgroundTask_TYPE
 * raw_url: StopBackgroundTask_RAW_URL
 * @param id - 
 */
export const StopBackgroundTask = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/background-task/{id}/stop'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('delete', domain + path, body, queryParameters, form, config)
}
export const StopBackgroundTask_RAW_URL = function() {
  return '/background-task/{id}/stop'
}
export const StopBackgroundTask_TYPE = function() {
  return 'delete'
}
export const StopBackgroundTaskURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/background-task/{id}/stop'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: RemoveBackgroundTask
 * url: RemoveBackgroundTaskURL
 * method: RemoveBackgroundTask_TYPE
 * raw_url: RemoveBackgroundTask_RAW_URL
 * @param id - 
 */
export const RemoveBackgroundTask = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/background-task/{id}'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('delete', domain + path, body, queryParameters, form, config)
}
export const RemoveBackgroundTask_RAW_URL = function() {
  return '/background-task/{id}'
}
export const RemoveBackgroundTask_TYPE = function() {
  return 'delete'
}
export const RemoveBackgroundTaskURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/background-task/{id}'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetBiliBiliFavorites
 * url: GetBiliBiliFavoritesURL
 * method: GetBiliBiliFavorites_TYPE
 * raw_url: GetBiliBiliFavorites_RAW_URL
 */
export const GetBiliBiliFavorites = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/bilibili/favorites'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetBiliBiliFavorites_RAW_URL = function() {
  return '/bilibili/favorites'
}
export const GetBiliBiliFavorites_TYPE = function() {
  return 'get'
}
export const GetBiliBiliFavoritesURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/bilibili/favorites'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetComponentDescriptors
 * url: GetComponentDescriptorsURL
 * method: GetComponentDescriptors_TYPE
 * raw_url: GetComponentDescriptors_RAW_URL
 * @param type - 
 * @param additionalItems - 
 */
export const GetComponentDescriptors = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/component'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['type'] !== undefined) {
    queryParameters['type'] = parameters['type']
  }
  if (parameters['additionalItems'] !== undefined) {
    queryParameters['additionalItems'] = parameters['additionalItems']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetComponentDescriptors_RAW_URL = function() {
  return '/component'
}
export const GetComponentDescriptors_TYPE = function() {
  return 'get'
}
export const GetComponentDescriptorsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/component'
  if (parameters['type'] !== undefined) {
    queryParameters['type'] = parameters['type']
  }
  if (parameters['additionalItems'] !== undefined) {
    queryParameters['additionalItems'] = parameters['additionalItems']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetComponentDescriptorByKey
 * url: GetComponentDescriptorByKeyURL
 * method: GetComponentDescriptorByKey_TYPE
 * raw_url: GetComponentDescriptorByKey_RAW_URL
 * @param key - 
 * @param additionalItems - 
 */
export const GetComponentDescriptorByKey = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/component/key'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['key'] !== undefined) {
    queryParameters['key'] = parameters['key']
  }
  if (parameters['additionalItems'] !== undefined) {
    queryParameters['additionalItems'] = parameters['additionalItems']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetComponentDescriptorByKey_RAW_URL = function() {
  return '/component/key'
}
export const GetComponentDescriptorByKey_TYPE = function() {
  return 'get'
}
export const GetComponentDescriptorByKeyURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/component/key'
  if (parameters['key'] !== undefined) {
    queryParameters['key'] = parameters['key']
  }
  if (parameters['additionalItems'] !== undefined) {
    queryParameters['additionalItems'] = parameters['additionalItems']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: AddComponentOptions
 * url: AddComponentOptionsURL
 * method: AddComponentOptions_TYPE
 * raw_url: AddComponentOptions_RAW_URL
 * @param model - 
 */
export const AddComponentOptions = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/component-options'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('post', domain + path, body, queryParameters, form, config)
}
export const AddComponentOptions_RAW_URL = function() {
  return '/component-options'
}
export const AddComponentOptions_TYPE = function() {
  return 'post'
}
export const AddComponentOptionsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/component-options'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: PutComponentOptions
 * url: PutComponentOptionsURL
 * method: PutComponentOptions_TYPE
 * raw_url: PutComponentOptions_RAW_URL
 * @param id - 
 * @param model - 
 */
export const PutComponentOptions = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/component-options/{id}'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('put', domain + path, body, queryParameters, form, config)
}
export const PutComponentOptions_RAW_URL = function() {
  return '/component-options/{id}'
}
export const PutComponentOptions_TYPE = function() {
  return 'put'
}
export const PutComponentOptionsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/component-options/{id}'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: RemoveComponentOptions
 * url: RemoveComponentOptionsURL
 * method: RemoveComponentOptions_TYPE
 * raw_url: RemoveComponentOptions_RAW_URL
 * @param id - 
 */
export const RemoveComponentOptions = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/component-options/{id}'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('delete', domain + path, body, queryParameters, form, config)
}
export const RemoveComponentOptions_RAW_URL = function() {
  return '/component-options/{id}'
}
export const RemoveComponentOptions_TYPE = function() {
  return 'delete'
}
export const RemoveComponentOptionsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/component-options/{id}'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetAllExtensionMediaTypes
 * url: GetAllExtensionMediaTypesURL
 * method: GetAllExtensionMediaTypes_TYPE
 * raw_url: GetAllExtensionMediaTypes_RAW_URL
 */
export const GetAllExtensionMediaTypes = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/api/constant/extension-media-types'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetAllExtensionMediaTypes_RAW_URL = function() {
  return '/api/constant/extension-media-types'
}
export const GetAllExtensionMediaTypes_TYPE = function() {
  return 'get'
}
export const GetAllExtensionMediaTypesURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/api/constant/extension-media-types'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: getApiConstant
 * url: getApiConstantURL
 * method: getApiConstant_TYPE
 * raw_url: getApiConstant_RAW_URL
 */
export const getApiConstant = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/api/constant'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const getApiConstant_RAW_URL = function() {
  return '/api/constant'
}
export const getApiConstant_TYPE = function() {
  return 'get'
}
export const getApiConstantURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/api/constant'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetStatistics
 * url: GetStatisticsURL
 * method: GetStatistics_TYPE
 * raw_url: GetStatistics_RAW_URL
 */
export const GetStatistics = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/dashboard'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetStatistics_RAW_URL = function() {
  return '/dashboard'
}
export const GetStatistics_TYPE = function() {
  return 'get'
}
export const GetStatisticsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/dashboard'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetAllDownloaderNamingDefinitions
 * url: GetAllDownloaderNamingDefinitionsURL
 * method: GetAllDownloaderNamingDefinitions_TYPE
 * raw_url: GetAllDownloaderNamingDefinitions_RAW_URL
 */
export const GetAllDownloaderNamingDefinitions = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/download-task/downloader/naming-definitions'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetAllDownloaderNamingDefinitions_RAW_URL = function() {
  return '/download-task/downloader/naming-definitions'
}
export const GetAllDownloaderNamingDefinitions_TYPE = function() {
  return 'get'
}
export const GetAllDownloaderNamingDefinitionsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/download-task/downloader/naming-definitions'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetAllDownloadTasks
 * url: GetAllDownloadTasksURL
 * method: GetAllDownloadTasks_TYPE
 * raw_url: GetAllDownloadTasks_RAW_URL
 */
export const GetAllDownloadTasks = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/download-task'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetAllDownloadTasks_RAW_URL = function() {
  return '/download-task'
}
export const GetAllDownloadTasks_TYPE = function() {
  return 'get'
}
export const GetAllDownloadTasksURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/download-task'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: CreateDownloadTask
 * url: CreateDownloadTaskURL
 * method: CreateDownloadTask_TYPE
 * raw_url: CreateDownloadTask_RAW_URL
 * @param model - 
 */
export const CreateDownloadTask = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/download-task'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('post', domain + path, body, queryParameters, form, config)
}
export const CreateDownloadTask_RAW_URL = function() {
  return '/download-task'
}
export const CreateDownloadTask_TYPE = function() {
  return 'post'
}
export const CreateDownloadTaskURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/download-task'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetDownloadTask
 * url: GetDownloadTaskURL
 * method: GetDownloadTask_TYPE
 * raw_url: GetDownloadTask_RAW_URL
 * @param id - 
 */
export const GetDownloadTask = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/download-task/{id}'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetDownloadTask_RAW_URL = function() {
  return '/download-task/{id}'
}
export const GetDownloadTask_TYPE = function() {
  return 'get'
}
export const GetDownloadTaskURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/download-task/{id}'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: RemoveDownloadTask
 * url: RemoveDownloadTaskURL
 * method: RemoveDownloadTask_TYPE
 * raw_url: RemoveDownloadTask_RAW_URL
 * @param id - 
 */
export const RemoveDownloadTask = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/download-task/{id}'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('delete', domain + path, body, queryParameters, form, config)
}
export const RemoveDownloadTask_RAW_URL = function() {
  return '/download-task/{id}'
}
export const RemoveDownloadTask_TYPE = function() {
  return 'delete'
}
export const RemoveDownloadTaskURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/download-task/{id}'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: PutDownloadTask
 * url: PutDownloadTaskURL
 * method: PutDownloadTask_TYPE
 * raw_url: PutDownloadTask_RAW_URL
 * @param id - 
 * @param model - 
 */
export const PutDownloadTask = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/download-task/{id}'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('put', domain + path, body, queryParameters, form, config)
}
export const PutDownloadTask_RAW_URL = function() {
  return '/download-task/{id}'
}
export const PutDownloadTask_TYPE = function() {
  return 'put'
}
export const PutDownloadTaskURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/download-task/{id}'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: RemoveDownloadTasksByIds
 * url: RemoveDownloadTasksByIdsURL
 * method: RemoveDownloadTasksByIds_TYPE
 * raw_url: RemoveDownloadTasksByIds_RAW_URL
 * @param model - 
 */
export const RemoveDownloadTasksByIds = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/download-task/ids'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('delete', domain + path, body, queryParameters, form, config)
}
export const RemoveDownloadTasksByIds_RAW_URL = function() {
  return '/download-task/ids'
}
export const RemoveDownloadTasksByIds_TYPE = function() {
  return 'delete'
}
export const RemoveDownloadTasksByIdsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/download-task/ids'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: StartDownloadTasks
 * url: StartDownloadTasksURL
 * method: StartDownloadTasks_TYPE
 * raw_url: StartDownloadTasks_RAW_URL
 * @param model - 
 */
export const StartDownloadTasks = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/download-task/download'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('post', domain + path, body, queryParameters, form, config)
}
export const StartDownloadTasks_RAW_URL = function() {
  return '/download-task/download'
}
export const StartDownloadTasks_TYPE = function() {
  return 'post'
}
export const StartDownloadTasksURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/download-task/download'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: StopDownloadTasks
 * url: StopDownloadTasksURL
 * method: StopDownloadTasks_TYPE
 * raw_url: StopDownloadTasks_RAW_URL
 * @param model - 
 */
export const StopDownloadTasks = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/download-task/download'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('delete', domain + path, body, queryParameters, form, config)
}
export const StopDownloadTasks_RAW_URL = function() {
  return '/download-task/download'
}
export const StopDownloadTasks_TYPE = function() {
  return 'delete'
}
export const StopDownloadTasksURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/download-task/download'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetResourceEnhancementRecords
 * url: GetResourceEnhancementRecordsURL
 * method: GetResourceEnhancementRecords_TYPE
 * raw_url: GetResourceEnhancementRecords_RAW_URL
 * @param id - 
 */
export const GetResourceEnhancementRecords = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/resource/{id}/enhancement'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetResourceEnhancementRecords_RAW_URL = function() {
  return '/resource/{id}/enhancement'
}
export const GetResourceEnhancementRecords_TYPE = function() {
  return 'get'
}
export const GetResourceEnhancementRecordsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/resource/{id}/enhancement'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: EnhanceResource
 * url: EnhanceResourceURL
 * method: EnhanceResource_TYPE
 * raw_url: EnhanceResource_RAW_URL
 * @param id - 
 */
export const EnhanceResource = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/resource/{id}/enhancement'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('post', domain + path, body, queryParameters, form, config)
}
export const EnhanceResource_RAW_URL = function() {
  return '/resource/{id}/enhancement'
}
export const EnhanceResource_TYPE = function() {
  return 'post'
}
export const EnhanceResourceURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/resource/{id}/enhancement'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: RemoveResourceEnhancementRecords
 * url: RemoveResourceEnhancementRecordsURL
 * method: RemoveResourceEnhancementRecords_TYPE
 * raw_url: RemoveResourceEnhancementRecords_RAW_URL
 * @param id - 
 */
export const RemoveResourceEnhancementRecords = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/resource/{id}/enhancement'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('delete', domain + path, body, queryParameters, form, config)
}
export const RemoveResourceEnhancementRecords_RAW_URL = function() {
  return '/resource/{id}/enhancement'
}
export const RemoveResourceEnhancementRecords_TYPE = function() {
  return 'delete'
}
export const RemoveResourceEnhancementRecordsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/resource/{id}/enhancement'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: RemoveMediaLibraryEnhancementRecords
 * url: RemoveMediaLibraryEnhancementRecordsURL
 * method: RemoveMediaLibraryEnhancementRecords_TYPE
 * raw_url: RemoveMediaLibraryEnhancementRecords_RAW_URL
 * @param id - 
 */
export const RemoveMediaLibraryEnhancementRecords = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/media-library/{id}/enhancement'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('delete', domain + path, body, queryParameters, form, config)
}
export const RemoveMediaLibraryEnhancementRecords_RAW_URL = function() {
  return '/media-library/{id}/enhancement'
}
export const RemoveMediaLibraryEnhancementRecords_TYPE = function() {
  return 'delete'
}
export const RemoveMediaLibraryEnhancementRecordsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/media-library/{id}/enhancement'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: RemoveCategoryEnhancementRecords
 * url: RemoveCategoryEnhancementRecordsURL
 * method: RemoveCategoryEnhancementRecords_TYPE
 * raw_url: RemoveCategoryEnhancementRecords_RAW_URL
 * @param id - 
 */
export const RemoveCategoryEnhancementRecords = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/category/{id}/enhancement'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('delete', domain + path, body, queryParameters, form, config)
}
export const RemoveCategoryEnhancementRecords_RAW_URL = function() {
  return '/category/{id}/enhancement'
}
export const RemoveCategoryEnhancementRecords_TYPE = function() {
  return 'delete'
}
export const RemoveCategoryEnhancementRecordsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/category/{id}/enhancement'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: SearchEnhancementRecords
 * url: SearchEnhancementRecordsURL
 * method: SearchEnhancementRecords_TYPE
 * raw_url: SearchEnhancementRecords_RAW_URL
 * @param resourceId - 
 * @param success - 
 * @param enhancerDescriptorId - 
 * @param pageIndex - 
 * @param pageSize - 
 * @param skipCount - 
 */
export const SearchEnhancementRecords = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/enhancement-record'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['resourceId'] !== undefined) {
    queryParameters['resourceId'] = parameters['resourceId']
  }
  if (parameters['success'] !== undefined) {
    queryParameters['success'] = parameters['success']
  }
  if (parameters['enhancerDescriptorId'] !== undefined) {
    queryParameters['enhancerDescriptorId'] = parameters['enhancerDescriptorId']
  }
  if (parameters['pageIndex'] !== undefined) {
    queryParameters['pageIndex'] = parameters['pageIndex']
  }
  if (parameters['pageSize'] !== undefined) {
    queryParameters['pageSize'] = parameters['pageSize']
  }
  if (parameters['skipCount'] !== undefined) {
    queryParameters['skipCount'] = parameters['skipCount']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const SearchEnhancementRecords_RAW_URL = function() {
  return '/enhancement-record'
}
export const SearchEnhancementRecords_TYPE = function() {
  return 'get'
}
export const SearchEnhancementRecordsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/enhancement-record'
  if (parameters['resourceId'] !== undefined) {
    queryParameters['resourceId'] = parameters['resourceId']
  }
  if (parameters['success'] !== undefined) {
    queryParameters['success'] = parameters['success']
  }
  if (parameters['enhancerDescriptorId'] !== undefined) {
    queryParameters['enhancerDescriptorId'] = parameters['enhancerDescriptorId']
  }
  if (parameters['pageIndex'] !== undefined) {
    queryParameters['pageIndex'] = parameters['pageIndex']
  }
  if (parameters['pageSize'] !== undefined) {
    queryParameters['pageSize'] = parameters['pageSize']
  }
  if (parameters['skipCount'] !== undefined) {
    queryParameters['skipCount'] = parameters['skipCount']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetAllFavorites
 * url: GetAllFavoritesURL
 * method: GetAllFavorites_TYPE
 * raw_url: GetAllFavorites_RAW_URL
 */
export const GetAllFavorites = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/favorites'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetAllFavorites_RAW_URL = function() {
  return '/favorites'
}
export const GetAllFavorites_TYPE = function() {
  return 'get'
}
export const GetAllFavoritesURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/favorites'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: AddFavorites
 * url: AddFavoritesURL
 * method: AddFavorites_TYPE
 * raw_url: AddFavorites_RAW_URL
 * @param model - 
 */
export const AddFavorites = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/favorites'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('post', domain + path, body, queryParameters, form, config)
}
export const AddFavorites_RAW_URL = function() {
  return '/favorites'
}
export const AddFavorites_TYPE = function() {
  return 'post'
}
export const AddFavoritesURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/favorites'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: PutFavorites
 * url: PutFavoritesURL
 * method: PutFavorites_TYPE
 * raw_url: PutFavorites_RAW_URL
 * @param id - 
 * @param model - 
 */
export const PutFavorites = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/favorites/{id}'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('patch', domain + path, body, queryParameters, form, config)
}
export const PutFavorites_RAW_URL = function() {
  return '/favorites/{id}'
}
export const PutFavorites_TYPE = function() {
  return 'patch'
}
export const PutFavoritesURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/favorites/{id}'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: DeleteFavorites
 * url: DeleteFavoritesURL
 * method: DeleteFavorites_TYPE
 * raw_url: DeleteFavorites_RAW_URL
 * @param id - 
 */
export const DeleteFavorites = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/favorites/{id}'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('delete', domain + path, body, queryParameters, form, config)
}
export const DeleteFavorites_RAW_URL = function() {
  return '/favorites/{id}'
}
export const DeleteFavorites_TYPE = function() {
  return 'delete'
}
export const DeleteFavoritesURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/favorites/{id}'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: AddResourceToFavorites
 * url: AddResourceToFavoritesURL
 * method: AddResourceToFavorites_TYPE
 * raw_url: AddResourceToFavorites_RAW_URL
 * @param id - 
 * @param model - 
 */
export const AddResourceToFavorites = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/favorites/{id}/resource'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('post', domain + path, body, queryParameters, form, config)
}
export const AddResourceToFavorites_RAW_URL = function() {
  return '/favorites/{id}/resource'
}
export const AddResourceToFavorites_TYPE = function() {
  return 'post'
}
export const AddResourceToFavoritesURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/favorites/{id}/resource'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: DeleteResourceFromFavorites
 * url: DeleteResourceFromFavoritesURL
 * method: DeleteResourceFromFavorites_TYPE
 * raw_url: DeleteResourceFromFavorites_RAW_URL
 * @param id - 
 * @param model - 
 */
export const DeleteResourceFromFavorites = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/favorites/{id}/resource'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('delete', domain + path, body, queryParameters, form, config)
}
export const DeleteResourceFromFavorites_RAW_URL = function() {
  return '/favorites/{id}/resource'
}
export const DeleteResourceFromFavorites_TYPE = function() {
  return 'delete'
}
export const DeleteResourceFromFavoritesURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/favorites/{id}/resource'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: PutResourcesFavorites
 * url: PutResourcesFavoritesURL
 * method: PutResourcesFavorites_TYPE
 * raw_url: PutResourcesFavorites_RAW_URL
 * @param model - 
 */
export const PutResourcesFavorites = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/favorites/resource-mapping'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('put', domain + path, body, queryParameters, form, config)
}
export const PutResourcesFavorites_RAW_URL = function() {
  return '/favorites/resource-mapping'
}
export const PutResourcesFavorites_TYPE = function() {
  return 'put'
}
export const PutResourcesFavoritesURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/favorites/resource-mapping'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetEntryTaskInfo
 * url: GetEntryTaskInfoURL
 * method: GetEntryTaskInfo_TYPE
 * raw_url: GetEntryTaskInfo_RAW_URL
 * @param path - 
 */
export const GetEntryTaskInfo = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/file/task-info'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['path'] !== undefined) {
    queryParameters['path'] = parameters['path']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetEntryTaskInfo_RAW_URL = function() {
  return '/file/task-info'
}
export const GetEntryTaskInfo_TYPE = function() {
  return 'get'
}
export const GetEntryTaskInfoURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/file/task-info'
  if (parameters['path'] !== undefined) {
    queryParameters['path'] = parameters['path']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetIwFsInfo
 * url: GetIwFsInfoURL
 * method: GetIwFsInfo_TYPE
 * raw_url: GetIwFsInfo_RAW_URL
 * @param path - 
 */
export const GetIwFsInfo = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/file/iwfs-info'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['path'] !== undefined) {
    queryParameters['path'] = parameters['path']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetIwFsInfo_RAW_URL = function() {
  return '/file/iwfs-info'
}
export const GetIwFsInfo_TYPE = function() {
  return 'get'
}
export const GetIwFsInfoURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/file/iwfs-info'
  if (parameters['path'] !== undefined) {
    queryParameters['path'] = parameters['path']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetIwFsEntry
 * url: GetIwFsEntryURL
 * method: GetIwFsEntry_TYPE
 * raw_url: GetIwFsEntry_RAW_URL
 * @param path - 
 */
export const GetIwFsEntry = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/file/iwfs-entry'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['path'] !== undefined) {
    queryParameters['path'] = parameters['path']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetIwFsEntry_RAW_URL = function() {
  return '/file/iwfs-entry'
}
export const GetIwFsEntry_TYPE = function() {
  return 'get'
}
export const GetIwFsEntryURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/file/iwfs-entry'
  if (parameters['path'] !== undefined) {
    queryParameters['path'] = parameters['path']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: CreateDirectory
 * url: CreateDirectoryURL
 * method: CreateDirectory_TYPE
 * raw_url: CreateDirectory_RAW_URL
 * @param parent - 
 */
export const CreateDirectory = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/file/directory'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['parent'] !== undefined) {
    queryParameters['parent'] = parameters['parent']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('post', domain + path, body, queryParameters, form, config)
}
export const CreateDirectory_RAW_URL = function() {
  return '/file/directory'
}
export const CreateDirectory_TYPE = function() {
  return 'post'
}
export const CreateDirectoryURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/file/directory'
  if (parameters['parent'] !== undefined) {
    queryParameters['parent'] = parameters['parent']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetChildrenIwFsInfo
 * url: GetChildrenIwFsInfoURL
 * method: GetChildrenIwFsInfo_TYPE
 * raw_url: GetChildrenIwFsInfo_RAW_URL
 * @param root - 
 */
export const GetChildrenIwFsInfo = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/file/children/iwfs-info'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['root'] !== undefined) {
    queryParameters['root'] = parameters['root']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetChildrenIwFsInfo_RAW_URL = function() {
  return '/file/children/iwfs-info'
}
export const GetChildrenIwFsInfo_TYPE = function() {
  return 'get'
}
export const GetChildrenIwFsInfoURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/file/children/iwfs-info'
  if (parameters['root'] !== undefined) {
    queryParameters['root'] = parameters['root']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: RemoveFiles
 * url: RemoveFilesURL
 * method: RemoveFiles_TYPE
 * raw_url: RemoveFiles_RAW_URL
 * @param model - 
 */
export const RemoveFiles = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/file'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('delete', domain + path, body, queryParameters, form, config)
}
export const RemoveFiles_RAW_URL = function() {
  return '/file'
}
export const RemoveFiles_TYPE = function() {
  return 'delete'
}
export const RemoveFilesURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/file'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: RenameFile
 * url: RenameFileURL
 * method: RenameFile_TYPE
 * raw_url: RenameFile_RAW_URL
 * @param model - 
 */
export const RenameFile = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/file/name'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('put', domain + path, body, queryParameters, form, config)
}
export const RenameFile_RAW_URL = function() {
  return '/file/name'
}
export const RenameFile_TYPE = function() {
  return 'put'
}
export const RenameFileURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/file/name'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: OpenRecycleBin
 * url: OpenRecycleBinURL
 * method: OpenRecycleBin_TYPE
 * raw_url: OpenRecycleBin_RAW_URL
 */
export const OpenRecycleBin = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/file/recycle-bin'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const OpenRecycleBin_RAW_URL = function() {
  return '/file/recycle-bin'
}
export const OpenRecycleBin_TYPE = function() {
  return 'get'
}
export const OpenRecycleBinURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/file/recycle-bin'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: ExtractAndRemoveDirectory
 * url: ExtractAndRemoveDirectoryURL
 * method: ExtractAndRemoveDirectory_TYPE
 * raw_url: ExtractAndRemoveDirectory_RAW_URL
 * @param directory - 
 */
export const ExtractAndRemoveDirectory = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/file/extract-and-remove-directory'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['directory'] !== undefined) {
    queryParameters['directory'] = parameters['directory']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('post', domain + path, body, queryParameters, form, config)
}
export const ExtractAndRemoveDirectory_RAW_URL = function() {
  return '/file/extract-and-remove-directory'
}
export const ExtractAndRemoveDirectory_TYPE = function() {
  return 'post'
}
export const ExtractAndRemoveDirectoryURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/file/extract-and-remove-directory'
  if (parameters['directory'] !== undefined) {
    queryParameters['directory'] = parameters['directory']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: MoveEntries
 * url: MoveEntriesURL
 * method: MoveEntries_TYPE
 * raw_url: MoveEntries_RAW_URL
 * @param model - 
 */
export const MoveEntries = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/file/move-entries'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('post', domain + path, body, queryParameters, form, config)
}
export const MoveEntries_RAW_URL = function() {
  return '/file/move-entries'
}
export const MoveEntries_TYPE = function() {
  return 'post'
}
export const MoveEntriesURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/file/move-entries'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetSameNameEntriesInWorkingDirectory
 * url: GetSameNameEntriesInWorkingDirectoryURL
 * method: GetSameNameEntriesInWorkingDirectory_TYPE
 * raw_url: GetSameNameEntriesInWorkingDirectory_RAW_URL
 * @param model - 
 */
export const GetSameNameEntriesInWorkingDirectory = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/file/same-name-entries-in-working-directory'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('post', domain + path, body, queryParameters, form, config)
}
export const GetSameNameEntriesInWorkingDirectory_RAW_URL = function() {
  return '/file/same-name-entries-in-working-directory'
}
export const GetSameNameEntriesInWorkingDirectory_TYPE = function() {
  return 'post'
}
export const GetSameNameEntriesInWorkingDirectoryURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/file/same-name-entries-in-working-directory'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: RemoveSameNameEntryInWorkingDirectory
 * url: RemoveSameNameEntryInWorkingDirectoryURL
 * method: RemoveSameNameEntryInWorkingDirectory_TYPE
 * raw_url: RemoveSameNameEntryInWorkingDirectory_RAW_URL
 * @param model - 
 */
export const RemoveSameNameEntryInWorkingDirectory = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/file/same-name-entry-in-working-directory'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('delete', domain + path, body, queryParameters, form, config)
}
export const RemoveSameNameEntryInWorkingDirectory_RAW_URL = function() {
  return '/file/same-name-entry-in-working-directory'
}
export const RemoveSameNameEntryInWorkingDirectory_TYPE = function() {
  return 'delete'
}
export const RemoveSameNameEntryInWorkingDirectoryURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/file/same-name-entry-in-working-directory'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: StandardizeEntryName
 * url: StandardizeEntryNameURL
 * method: StandardizeEntryName_TYPE
 * raw_url: StandardizeEntryName_RAW_URL
 * @param path - 
 */
export const StandardizeEntryName = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/file/standardize'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['path'] !== undefined) {
    queryParameters['path'] = parameters['path']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('put', domain + path, body, queryParameters, form, config)
}
export const StandardizeEntryName_RAW_URL = function() {
  return '/file/standardize'
}
export const StandardizeEntryName_TYPE = function() {
  return 'put'
}
export const StandardizeEntryNameURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/file/standardize'
  if (parameters['path'] !== undefined) {
    queryParameters['path'] = parameters['path']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: PlayFile
 * url: PlayFileURL
 * method: PlayFile_TYPE
 * raw_url: PlayFile_RAW_URL
 * @param fullname - 
 */
export const PlayFile = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/file/play'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['fullname'] !== undefined) {
    queryParameters['fullname'] = parameters['fullname']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const PlayFile_RAW_URL = function() {
  return '/file/play'
}
export const PlayFile_TYPE = function() {
  return 'get'
}
export const PlayFileURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/file/play'
  if (parameters['fullname'] !== undefined) {
    queryParameters['fullname'] = parameters['fullname']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: DecompressFiles
 * url: DecompressFilesURL
 * method: DecompressFiles_TYPE
 * raw_url: DecompressFiles_RAW_URL
 * @param model - 
 */
export const DecompressFiles = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/file/decompression'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('post', domain + path, body, queryParameters, form, config)
}
export const DecompressFiles_RAW_URL = function() {
  return '/file/decompression'
}
export const DecompressFiles_TYPE = function() {
  return 'post'
}
export const DecompressFilesURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/file/decompression'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetIconData
 * url: GetIconDataURL
 * method: GetIconData_TYPE
 * raw_url: GetIconData_RAW_URL
 * @param path - 
 */
export const GetIconData = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/file/icon'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['path'] !== undefined) {
    queryParameters['path'] = parameters['path']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetIconData_RAW_URL = function() {
  return '/file/icon'
}
export const GetIconData_TYPE = function() {
  return 'get'
}
export const GetIconDataURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/file/icon'
  if (parameters['path'] !== undefined) {
    queryParameters['path'] = parameters['path']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetAllFiles
 * url: GetAllFilesURL
 * method: GetAllFiles_TYPE
 * raw_url: GetAllFiles_RAW_URL
 * @param path - 
 */
export const GetAllFiles = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/file/all-files'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['path'] !== undefined) {
    queryParameters['path'] = parameters['path']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetAllFiles_RAW_URL = function() {
  return '/file/all-files'
}
export const GetAllFiles_TYPE = function() {
  return 'get'
}
export const GetAllFilesURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/file/all-files'
  if (parameters['path'] !== undefined) {
    queryParameters['path'] = parameters['path']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetCompressedFileEntries
 * url: GetCompressedFileEntriesURL
 * method: GetCompressedFileEntries_TYPE
 * raw_url: GetCompressedFileEntries_RAW_URL
 * @param compressedFilePath - 
 */
export const GetCompressedFileEntries = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/file/compressed-file/entries'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['compressedFilePath'] !== undefined) {
    queryParameters['compressedFilePath'] = parameters['compressedFilePath']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetCompressedFileEntries_RAW_URL = function() {
  return '/file/compressed-file/entries'
}
export const GetCompressedFileEntries_TYPE = function() {
  return 'get'
}
export const GetCompressedFileEntriesURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/file/compressed-file/entries'
  if (parameters['compressedFilePath'] !== undefined) {
    queryParameters['compressedFilePath'] = parameters['compressedFilePath']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetFileExtensionCounts
 * url: GetFileExtensionCountsURL
 * method: GetFileExtensionCounts_TYPE
 * raw_url: GetFileExtensionCounts_RAW_URL
 * @param sampleFile - 
 * @param rootPath - 
 */
export const GetFileExtensionCounts = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/file/file-extension-counts'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['sampleFile'] !== undefined) {
    queryParameters['sampleFile'] = parameters['sampleFile']
  }
  if (parameters['rootPath'] !== undefined) {
    queryParameters['rootPath'] = parameters['rootPath']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetFileExtensionCounts_RAW_URL = function() {
  return '/file/file-extension-counts'
}
export const GetFileExtensionCounts_TYPE = function() {
  return 'get'
}
export const GetFileExtensionCountsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/file/file-extension-counts'
  if (parameters['sampleFile'] !== undefined) {
    queryParameters['sampleFile'] = parameters['sampleFile']
  }
  if (parameters['rootPath'] !== undefined) {
    queryParameters['rootPath'] = parameters['rootPath']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: PreviewFileEntriesMergeResult
 * url: PreviewFileEntriesMergeResultURL
 * method: PreviewFileEntriesMergeResult_TYPE
 * raw_url: PreviewFileEntriesMergeResult_RAW_URL
 * @param model - 
 */
export const PreviewFileEntriesMergeResult = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/file/merge-preview'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('put', domain + path, body, queryParameters, form, config)
}
export const PreviewFileEntriesMergeResult_RAW_URL = function() {
  return '/file/merge-preview'
}
export const PreviewFileEntriesMergeResult_TYPE = function() {
  return 'put'
}
export const PreviewFileEntriesMergeResultURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/file/merge-preview'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: PreviewFileEntriesMergeResultInRootPath
 * url: PreviewFileEntriesMergeResultInRootPathURL
 * method: PreviewFileEntriesMergeResultInRootPath_TYPE
 * raw_url: PreviewFileEntriesMergeResultInRootPath_RAW_URL
 * @param model - 
 */
export const PreviewFileEntriesMergeResultInRootPath = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/file/merge-preview-in-root-path'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('put', domain + path, body, queryParameters, form, config)
}
export const PreviewFileEntriesMergeResultInRootPath_RAW_URL = function() {
  return '/file/merge-preview-in-root-path'
}
export const PreviewFileEntriesMergeResultInRootPath_TYPE = function() {
  return 'put'
}
export const PreviewFileEntriesMergeResultInRootPathURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/file/merge-preview-in-root-path'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: MergeFileEntries
 * url: MergeFileEntriesURL
 * method: MergeFileEntries_TYPE
 * raw_url: MergeFileEntries_RAW_URL
 * @param model - 
 */
export const MergeFileEntries = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/file/merge'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('put', domain + path, body, queryParameters, form, config)
}
export const MergeFileEntries_RAW_URL = function() {
  return '/file/merge'
}
export const MergeFileEntries_TYPE = function() {
  return 'put'
}
export const MergeFileEntriesURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/file/merge'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: MergeFileEntriesInRootPath
 * url: MergeFileEntriesInRootPathURL
 * method: MergeFileEntriesInRootPath_TYPE
 * raw_url: MergeFileEntriesInRootPath_RAW_URL
 * @param model - 
 */
export const MergeFileEntriesInRootPath = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/file/merge-by'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('put', domain + path, body, queryParameters, form, config)
}
export const MergeFileEntriesInRootPath_RAW_URL = function() {
  return '/file/merge-by'
}
export const MergeFileEntriesInRootPath_TYPE = function() {
  return 'put'
}
export const MergeFileEntriesInRootPathURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/file/merge-by'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetFileSystemEntriesInDirectory
 * url: GetFileSystemEntriesInDirectoryURL
 * method: GetFileSystemEntriesInDirectory_TYPE
 * raw_url: GetFileSystemEntriesInDirectory_RAW_URL
 * @param path - 
 * @param maxCount - 
 */
export const GetFileSystemEntriesInDirectory = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/file/directory/file-entries'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['path'] !== undefined) {
    queryParameters['path'] = parameters['path']
  }
  if (parameters['maxCount'] !== undefined) {
    queryParameters['maxCount'] = parameters['maxCount']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetFileSystemEntriesInDirectory_RAW_URL = function() {
  return '/file/directory/file-entries'
}
export const GetFileSystemEntriesInDirectory_TYPE = function() {
  return 'get'
}
export const GetFileSystemEntriesInDirectoryURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/file/directory/file-entries'
  if (parameters['path'] !== undefined) {
    queryParameters['path'] = parameters['path']
  }
  if (parameters['maxCount'] !== undefined) {
    queryParameters['maxCount'] = parameters['maxCount']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: StartWatchingChangesInFileProcessorWorkspace
 * url: StartWatchingChangesInFileProcessorWorkspaceURL
 * method: StartWatchingChangesInFileProcessorWorkspace_TYPE
 * raw_url: StartWatchingChangesInFileProcessorWorkspace_RAW_URL
 * @param path - 
 */
export const StartWatchingChangesInFileProcessorWorkspace = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/file/file-processor-watcher'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['path'] !== undefined) {
    queryParameters['path'] = parameters['path']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('post', domain + path, body, queryParameters, form, config)
}
export const StartWatchingChangesInFileProcessorWorkspace_RAW_URL = function() {
  return '/file/file-processor-watcher'
}
export const StartWatchingChangesInFileProcessorWorkspace_TYPE = function() {
  return 'post'
}
export const StartWatchingChangesInFileProcessorWorkspaceURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/file/file-processor-watcher'
  if (parameters['path'] !== undefined) {
    queryParameters['path'] = parameters['path']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: StopWatchingChangesInFileProcessorWorkspace
 * url: StopWatchingChangesInFileProcessorWorkspaceURL
 * method: StopWatchingChangesInFileProcessorWorkspace_TYPE
 * raw_url: StopWatchingChangesInFileProcessorWorkspace_RAW_URL
 */
export const StopWatchingChangesInFileProcessorWorkspace = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/file/file-processor-watcher'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('delete', domain + path, body, queryParameters, form, config)
}
export const StopWatchingChangesInFileProcessorWorkspace_RAW_URL = function() {
  return '/file/file-processor-watcher'
}
export const StopWatchingChangesInFileProcessorWorkspace_TYPE = function() {
  return 'delete'
}
export const StopWatchingChangesInFileProcessorWorkspaceURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/file/file-processor-watcher'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: OpenFilesSelector
 * url: OpenFilesSelectorURL
 * method: OpenFilesSelector_TYPE
 * raw_url: OpenFilesSelector_RAW_URL
 * @param initialDirectory - 
 */
export const OpenFilesSelector = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/gui/files-selector'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['initialDirectory'] !== undefined) {
    queryParameters['initialDirectory'] = parameters['initialDirectory']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const OpenFilesSelector_RAW_URL = function() {
  return '/gui/files-selector'
}
export const OpenFilesSelector_TYPE = function() {
  return 'get'
}
export const OpenFilesSelectorURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/gui/files-selector'
  if (parameters['initialDirectory'] !== undefined) {
    queryParameters['initialDirectory'] = parameters['initialDirectory']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: OpenFileSelector
 * url: OpenFileSelectorURL
 * method: OpenFileSelector_TYPE
 * raw_url: OpenFileSelector_RAW_URL
 * @param initialDirectory - 
 */
export const OpenFileSelector = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/gui/file-selector'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['initialDirectory'] !== undefined) {
    queryParameters['initialDirectory'] = parameters['initialDirectory']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const OpenFileSelector_RAW_URL = function() {
  return '/gui/file-selector'
}
export const OpenFileSelector_TYPE = function() {
  return 'get'
}
export const OpenFileSelectorURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/gui/file-selector'
  if (parameters['initialDirectory'] !== undefined) {
    queryParameters['initialDirectory'] = parameters['initialDirectory']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: OpenFolderSelector
 * url: OpenFolderSelectorURL
 * method: OpenFolderSelector_TYPE
 * raw_url: OpenFolderSelector_RAW_URL
 * @param initialDirectory - 
 */
export const OpenFolderSelector = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/gui/folder-selector'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['initialDirectory'] !== undefined) {
    queryParameters['initialDirectory'] = parameters['initialDirectory']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const OpenFolderSelector_RAW_URL = function() {
  return '/gui/folder-selector'
}
export const OpenFolderSelector_TYPE = function() {
  return 'get'
}
export const OpenFolderSelectorURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/gui/folder-selector'
  if (parameters['initialDirectory'] !== undefined) {
    queryParameters['initialDirectory'] = parameters['initialDirectory']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: OpenUrlInDefaultBrowser
 * url: OpenUrlInDefaultBrowserURL
 * method: OpenUrlInDefaultBrowser_TYPE
 * raw_url: OpenUrlInDefaultBrowser_RAW_URL
 * @param url - 
 */
export const OpenUrlInDefaultBrowser = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/gui/url'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['url'] !== undefined) {
    queryParameters['url'] = parameters['url']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const OpenUrlInDefaultBrowser_RAW_URL = function() {
  return '/gui/url'
}
export const OpenUrlInDefaultBrowser_TYPE = function() {
  return 'get'
}
export const OpenUrlInDefaultBrowserURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/gui/url'
  if (parameters['url'] !== undefined) {
    queryParameters['url'] = parameters['url']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetAllLogs
 * url: GetAllLogsURL
 * method: GetAllLogs_TYPE
 * raw_url: GetAllLogs_RAW_URL
 */
export const GetAllLogs = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/log'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetAllLogs_RAW_URL = function() {
  return '/log'
}
export const GetAllLogs_TYPE = function() {
  return 'get'
}
export const GetAllLogsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/log'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: ClearAllLog
 * url: ClearAllLogURL
 * method: ClearAllLog_TYPE
 * raw_url: ClearAllLog_RAW_URL
 */
export const ClearAllLog = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/log'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('delete', domain + path, body, queryParameters, form, config)
}
export const ClearAllLog_RAW_URL = function() {
  return '/log'
}
export const ClearAllLog_TYPE = function() {
  return 'delete'
}
export const ClearAllLogURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/log'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: SearchLogs
 * url: SearchLogsURL
 * method: SearchLogs_TYPE
 * raw_url: SearchLogs_RAW_URL
 * @param level - 
 * @param startDt - 
 * @param endDt - 
 * @param logger - 
 * @param event - 
 * @param message - 
 * @param pageIndex - 
 * @param pageSize - 
 * @param skipCount - 
 */
export const SearchLogs = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/log/filtered'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['level'] !== undefined) {
    queryParameters['level'] = parameters['level']
  }
  if (parameters['startDt'] !== undefined) {
    queryParameters['startDt'] = parameters['startDt']
  }
  if (parameters['endDt'] !== undefined) {
    queryParameters['endDt'] = parameters['endDt']
  }
  if (parameters['logger'] !== undefined) {
    queryParameters['logger'] = parameters['logger']
  }
  if (parameters['event'] !== undefined) {
    queryParameters['event'] = parameters['event']
  }
  if (parameters['message'] !== undefined) {
    queryParameters['message'] = parameters['message']
  }
  if (parameters['pageIndex'] !== undefined) {
    queryParameters['pageIndex'] = parameters['pageIndex']
  }
  if (parameters['pageSize'] !== undefined) {
    queryParameters['pageSize'] = parameters['pageSize']
  }
  if (parameters['skipCount'] !== undefined) {
    queryParameters['skipCount'] = parameters['skipCount']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const SearchLogs_RAW_URL = function() {
  return '/log/filtered'
}
export const SearchLogs_TYPE = function() {
  return 'get'
}
export const SearchLogsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/log/filtered'
  if (parameters['level'] !== undefined) {
    queryParameters['level'] = parameters['level']
  }
  if (parameters['startDt'] !== undefined) {
    queryParameters['startDt'] = parameters['startDt']
  }
  if (parameters['endDt'] !== undefined) {
    queryParameters['endDt'] = parameters['endDt']
  }
  if (parameters['logger'] !== undefined) {
    queryParameters['logger'] = parameters['logger']
  }
  if (parameters['event'] !== undefined) {
    queryParameters['event'] = parameters['event']
  }
  if (parameters['message'] !== undefined) {
    queryParameters['message'] = parameters['message']
  }
  if (parameters['pageIndex'] !== undefined) {
    queryParameters['pageIndex'] = parameters['pageIndex']
  }
  if (parameters['pageSize'] !== undefined) {
    queryParameters['pageSize'] = parameters['pageSize']
  }
  if (parameters['skipCount'] !== undefined) {
    queryParameters['skipCount'] = parameters['skipCount']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetUnreadLogCount
 * url: GetUnreadLogCountURL
 * method: GetUnreadLogCount_TYPE
 * raw_url: GetUnreadLogCount_RAW_URL
 */
export const GetUnreadLogCount = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/log/unread/count'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetUnreadLogCount_RAW_URL = function() {
  return '/log/unread/count'
}
export const GetUnreadLogCount_TYPE = function() {
  return 'get'
}
export const GetUnreadLogCountURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/log/unread/count'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: ReadLog
 * url: ReadLogURL
 * method: ReadLog_TYPE
 * raw_url: ReadLog_RAW_URL
 * @param id - 
 */
export const ReadLog = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/log/{id}/read'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('patch', domain + path, body, queryParameters, form, config)
}
export const ReadLog_RAW_URL = function() {
  return '/log/{id}/read'
}
export const ReadLog_TYPE = function() {
  return 'patch'
}
export const ReadLogURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/log/{id}/read'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: ReadAllLog
 * url: ReadAllLogURL
 * method: ReadAllLog_TYPE
 * raw_url: ReadAllLog_RAW_URL
 */
export const ReadAllLog = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/log/read'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('patch', domain + path, body, queryParameters, form, config)
}
export const ReadAllLog_RAW_URL = function() {
  return '/log/read'
}
export const ReadAllLog_TYPE = function() {
  return 'patch'
}
export const ReadAllLogURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/log/read'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetAllMediaLibraries
 * url: GetAllMediaLibrariesURL
 * method: GetAllMediaLibraries_TYPE
 * raw_url: GetAllMediaLibraries_RAW_URL
 */
export const GetAllMediaLibraries = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/media-library'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetAllMediaLibraries_RAW_URL = function() {
  return '/media-library'
}
export const GetAllMediaLibraries_TYPE = function() {
  return 'get'
}
export const GetAllMediaLibrariesURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/media-library'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: AddMediaLibrary
 * url: AddMediaLibraryURL
 * method: AddMediaLibrary_TYPE
 * raw_url: AddMediaLibrary_RAW_URL
 * @param model - 
 */
export const AddMediaLibrary = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/media-library'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('post', domain + path, body, queryParameters, form, config)
}
export const AddMediaLibrary_RAW_URL = function() {
  return '/media-library'
}
export const AddMediaLibrary_TYPE = function() {
  return 'post'
}
export const AddMediaLibraryURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/media-library'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: RemoveMediaLibrary
 * url: RemoveMediaLibraryURL
 * method: RemoveMediaLibrary_TYPE
 * raw_url: RemoveMediaLibrary_RAW_URL
 * @param id - 
 */
export const RemoveMediaLibrary = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/media-library/{id}'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('delete', domain + path, body, queryParameters, form, config)
}
export const RemoveMediaLibrary_RAW_URL = function() {
  return '/media-library/{id}'
}
export const RemoveMediaLibrary_TYPE = function() {
  return 'delete'
}
export const RemoveMediaLibraryURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/media-library/{id}'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: PatchMediaLibrary
 * url: PatchMediaLibraryURL
 * method: PatchMediaLibrary_TYPE
 * raw_url: PatchMediaLibrary_RAW_URL
 * @param id - 
 * @param model - 
 */
export const PatchMediaLibrary = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/media-library/{id}'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('put', domain + path, body, queryParameters, form, config)
}
export const PatchMediaLibrary_RAW_URL = function() {
  return '/media-library/{id}'
}
export const PatchMediaLibrary_TYPE = function() {
  return 'put'
}
export const PatchMediaLibraryURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/media-library/{id}'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetMediaLibrarySyncStatus
 * url: GetMediaLibrarySyncStatusURL
 * method: GetMediaLibrarySyncStatus_TYPE
 * raw_url: GetMediaLibrarySyncStatus_RAW_URL
 */
export const GetMediaLibrarySyncStatus = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/media-library/sync/status'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetMediaLibrarySyncStatus_RAW_URL = function() {
  return '/media-library/sync/status'
}
export const GetMediaLibrarySyncStatus_TYPE = function() {
  return 'get'
}
export const GetMediaLibrarySyncStatusURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/media-library/sync/status'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: SyncMediaLibrary
 * url: SyncMediaLibraryURL
 * method: SyncMediaLibrary_TYPE
 * raw_url: SyncMediaLibrary_RAW_URL
 */
export const SyncMediaLibrary = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/media-library/sync'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('put', domain + path, body, queryParameters, form, config)
}
export const SyncMediaLibrary_RAW_URL = function() {
  return '/media-library/sync'
}
export const SyncMediaLibrary_TYPE = function() {
  return 'put'
}
export const SyncMediaLibraryURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/media-library/sync'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: StopSyncMediaLibrary
 * url: StopSyncMediaLibraryURL
 * method: StopSyncMediaLibrary_TYPE
 * raw_url: StopSyncMediaLibrary_RAW_URL
 */
export const StopSyncMediaLibrary = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/media-library/sync'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('delete', domain + path, body, queryParameters, form, config)
}
export const StopSyncMediaLibrary_RAW_URL = function() {
  return '/media-library/sync'
}
export const StopSyncMediaLibrary_TYPE = function() {
  return 'delete'
}
export const StopSyncMediaLibraryURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/media-library/sync'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: ValidatePathConfiguration
 * url: ValidatePathConfigurationURL
 * method: ValidatePathConfiguration_TYPE
 * raw_url: ValidatePathConfiguration_RAW_URL
 * @param model - 
 */
export const ValidatePathConfiguration = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/media-library/path-configuration-validation'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('post', domain + path, body, queryParameters, form, config)
}
export const ValidatePathConfiguration_RAW_URL = function() {
  return '/media-library/path-configuration-validation'
}
export const ValidatePathConfiguration_TYPE = function() {
  return 'post'
}
export const ValidatePathConfigurationURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/media-library/path-configuration-validation'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: SortMediaLibrariesInCategory
 * url: SortMediaLibrariesInCategoryURL
 * method: SortMediaLibrariesInCategory_TYPE
 * raw_url: SortMediaLibrariesInCategory_RAW_URL
 * @param model - 
 */
export const SortMediaLibrariesInCategory = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/media-library/orders-in-category'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('put', domain + path, body, queryParameters, form, config)
}
export const SortMediaLibrariesInCategory_RAW_URL = function() {
  return '/media-library/orders-in-category'
}
export const SortMediaLibrariesInCategory_TYPE = function() {
  return 'put'
}
export const SortMediaLibrariesInCategoryURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/media-library/orders-in-category'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: AddMediaLibraryPathConfiguration
 * url: AddMediaLibraryPathConfigurationURL
 * method: AddMediaLibraryPathConfiguration_TYPE
 * raw_url: AddMediaLibraryPathConfiguration_RAW_URL
 * @param id - 
 * @param model - 
 */
export const AddMediaLibraryPathConfiguration = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/media-library/{id}/path-configuration'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('post', domain + path, body, queryParameters, form, config)
}
export const AddMediaLibraryPathConfiguration_RAW_URL = function() {
  return '/media-library/{id}/path-configuration'
}
export const AddMediaLibraryPathConfiguration_TYPE = function() {
  return 'post'
}
export const AddMediaLibraryPathConfigurationURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/media-library/{id}/path-configuration'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: RemoveMediaLibraryPathConfiguration
 * url: RemoveMediaLibraryPathConfigurationURL
 * method: RemoveMediaLibraryPathConfiguration_TYPE
 * raw_url: RemoveMediaLibraryPathConfiguration_RAW_URL
 * @param id - 
 * @param model - 
 */
export const RemoveMediaLibraryPathConfiguration = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/media-library/{id}/path-configuration'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('delete', domain + path, body, queryParameters, form, config)
}
export const RemoveMediaLibraryPathConfiguration_RAW_URL = function() {
  return '/media-library/{id}/path-configuration'
}
export const RemoveMediaLibraryPathConfiguration_TYPE = function() {
  return 'delete'
}
export const RemoveMediaLibraryPathConfigurationURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/media-library/{id}/path-configuration'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetPathRelatedLibraries
 * url: GetPathRelatedLibrariesURL
 * method: GetPathRelatedLibraries_TYPE
 * raw_url: GetPathRelatedLibraries_RAW_URL
 * @param libraryId - 
 * @param currentPath - 
 * @param newPath - 
 */
export const GetPathRelatedLibraries = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/media-library/path-related-libraries'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['libraryId'] !== undefined) {
    queryParameters['libraryId'] = parameters['libraryId']
  }
  if (parameters['currentPath'] !== undefined) {
    queryParameters['currentPath'] = parameters['currentPath']
  }
  if (parameters['newPath'] !== undefined) {
    queryParameters['newPath'] = parameters['newPath']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetPathRelatedLibraries_RAW_URL = function() {
  return '/media-library/path-related-libraries'
}
export const GetPathRelatedLibraries_TYPE = function() {
  return 'get'
}
export const GetPathRelatedLibrariesURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/media-library/path-related-libraries'
  if (parameters['libraryId'] !== undefined) {
    queryParameters['libraryId'] = parameters['libraryId']
  }
  if (parameters['currentPath'] !== undefined) {
    queryParameters['currentPath'] = parameters['currentPath']
  }
  if (parameters['newPath'] !== undefined) {
    queryParameters['newPath'] = parameters['newPath']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetAppOptions
 * url: GetAppOptionsURL
 * method: GetAppOptions_TYPE
 * raw_url: GetAppOptions_RAW_URL
 */
export const GetAppOptions = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/options/app'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetAppOptions_RAW_URL = function() {
  return '/options/app'
}
export const GetAppOptions_TYPE = function() {
  return 'get'
}
export const GetAppOptionsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/options/app'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: PatchAppOptions
 * url: PatchAppOptionsURL
 * method: PatchAppOptions_TYPE
 * raw_url: PatchAppOptions_RAW_URL
 * @param model - 
 */
export const PatchAppOptions = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/options/app'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('patch', domain + path, body, queryParameters, form, config)
}
export const PatchAppOptions_RAW_URL = function() {
  return '/options/app'
}
export const PatchAppOptions_TYPE = function() {
  return 'patch'
}
export const PatchAppOptionsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/options/app'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetUIOptions
 * url: GetUIOptionsURL
 * method: GetUIOptions_TYPE
 * raw_url: GetUIOptions_RAW_URL
 */
export const GetUIOptions = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/options/ui'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetUIOptions_RAW_URL = function() {
  return '/options/ui'
}
export const GetUIOptions_TYPE = function() {
  return 'get'
}
export const GetUIOptionsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/options/ui'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: PatchUIOptions
 * url: PatchUIOptionsURL
 * method: PatchUIOptions_TYPE
 * raw_url: PatchUIOptions_RAW_URL
 * @param model - 
 */
export const PatchUIOptions = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/options/ui'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('patch', domain + path, body, queryParameters, form, config)
}
export const PatchUIOptions_RAW_URL = function() {
  return '/options/ui'
}
export const PatchUIOptions_TYPE = function() {
  return 'patch'
}
export const PatchUIOptionsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/options/ui'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetBilibiliOptions
 * url: GetBilibiliOptionsURL
 * method: GetBilibiliOptions_TYPE
 * raw_url: GetBilibiliOptions_RAW_URL
 */
export const GetBilibiliOptions = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/options/bilibili'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetBilibiliOptions_RAW_URL = function() {
  return '/options/bilibili'
}
export const GetBilibiliOptions_TYPE = function() {
  return 'get'
}
export const GetBilibiliOptionsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/options/bilibili'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: PatchBilibiliOptions
 * url: PatchBilibiliOptionsURL
 * method: PatchBilibiliOptions_TYPE
 * raw_url: PatchBilibiliOptions_RAW_URL
 * @param model - 
 */
export const PatchBilibiliOptions = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/options/bilibili'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('patch', domain + path, body, queryParameters, form, config)
}
export const PatchBilibiliOptions_RAW_URL = function() {
  return '/options/bilibili'
}
export const PatchBilibiliOptions_TYPE = function() {
  return 'patch'
}
export const PatchBilibiliOptionsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/options/bilibili'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetExHentaiOptions
 * url: GetExHentaiOptionsURL
 * method: GetExHentaiOptions_TYPE
 * raw_url: GetExHentaiOptions_RAW_URL
 */
export const GetExHentaiOptions = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/options/exhentai'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetExHentaiOptions_RAW_URL = function() {
  return '/options/exhentai'
}
export const GetExHentaiOptions_TYPE = function() {
  return 'get'
}
export const GetExHentaiOptionsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/options/exhentai'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: PatchExHentaiOptions
 * url: PatchExHentaiOptionsURL
 * method: PatchExHentaiOptions_TYPE
 * raw_url: PatchExHentaiOptions_RAW_URL
 * @param model - 
 */
export const PatchExHentaiOptions = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/options/exhentai'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('patch', domain + path, body, queryParameters, form, config)
}
export const PatchExHentaiOptions_RAW_URL = function() {
  return '/options/exhentai'
}
export const PatchExHentaiOptions_TYPE = function() {
  return 'patch'
}
export const PatchExHentaiOptionsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/options/exhentai'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetFileSystemOptions
 * url: GetFileSystemOptionsURL
 * method: GetFileSystemOptions_TYPE
 * raw_url: GetFileSystemOptions_RAW_URL
 */
export const GetFileSystemOptions = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/options/filesystem'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetFileSystemOptions_RAW_URL = function() {
  return '/options/filesystem'
}
export const GetFileSystemOptions_TYPE = function() {
  return 'get'
}
export const GetFileSystemOptionsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/options/filesystem'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: PatchFileSystemOptions
 * url: PatchFileSystemOptionsURL
 * method: PatchFileSystemOptions_TYPE
 * raw_url: PatchFileSystemOptions_RAW_URL
 * @param model - 
 */
export const PatchFileSystemOptions = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/options/filesystem'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('patch', domain + path, body, queryParameters, form, config)
}
export const PatchFileSystemOptions_RAW_URL = function() {
  return '/options/filesystem'
}
export const PatchFileSystemOptions_TYPE = function() {
  return 'patch'
}
export const PatchFileSystemOptionsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/options/filesystem'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetJavLibraryOptions
 * url: GetJavLibraryOptionsURL
 * method: GetJavLibraryOptions_TYPE
 * raw_url: GetJavLibraryOptions_RAW_URL
 */
export const GetJavLibraryOptions = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/options/javlibrary'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetJavLibraryOptions_RAW_URL = function() {
  return '/options/javlibrary'
}
export const GetJavLibraryOptions_TYPE = function() {
  return 'get'
}
export const GetJavLibraryOptionsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/options/javlibrary'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: PatchJavLibraryOptions
 * url: PatchJavLibraryOptionsURL
 * method: PatchJavLibraryOptions_TYPE
 * raw_url: PatchJavLibraryOptions_RAW_URL
 * @param model - 
 */
export const PatchJavLibraryOptions = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/options/javlibrary'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('patch', domain + path, body, queryParameters, form, config)
}
export const PatchJavLibraryOptions_RAW_URL = function() {
  return '/options/javlibrary'
}
export const PatchJavLibraryOptions_TYPE = function() {
  return 'patch'
}
export const PatchJavLibraryOptionsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/options/javlibrary'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetPixivOptions
 * url: GetPixivOptionsURL
 * method: GetPixivOptions_TYPE
 * raw_url: GetPixivOptions_RAW_URL
 */
export const GetPixivOptions = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/options/pixiv'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetPixivOptions_RAW_URL = function() {
  return '/options/pixiv'
}
export const GetPixivOptions_TYPE = function() {
  return 'get'
}
export const GetPixivOptionsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/options/pixiv'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: PatchPixivOptions
 * url: PatchPixivOptionsURL
 * method: PatchPixivOptions_TYPE
 * raw_url: PatchPixivOptions_RAW_URL
 * @param model - 
 */
export const PatchPixivOptions = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/options/pixiv'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('patch', domain + path, body, queryParameters, form, config)
}
export const PatchPixivOptions_RAW_URL = function() {
  return '/options/pixiv'
}
export const PatchPixivOptions_TYPE = function() {
  return 'patch'
}
export const PatchPixivOptionsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/options/pixiv'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetResourceOptions
 * url: GetResourceOptionsURL
 * method: GetResourceOptions_TYPE
 * raw_url: GetResourceOptions_RAW_URL
 */
export const GetResourceOptions = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/options/resource'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetResourceOptions_RAW_URL = function() {
  return '/options/resource'
}
export const GetResourceOptions_TYPE = function() {
  return 'get'
}
export const GetResourceOptionsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/options/resource'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: PatchResourceOptions
 * url: PatchResourceOptionsURL
 * method: PatchResourceOptions_TYPE
 * raw_url: PatchResourceOptions_RAW_URL
 * @param model - 
 */
export const PatchResourceOptions = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/options/resource'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('patch', domain + path, body, queryParameters, form, config)
}
export const PatchResourceOptions_RAW_URL = function() {
  return '/options/resource'
}
export const PatchResourceOptions_TYPE = function() {
  return 'patch'
}
export const PatchResourceOptionsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/options/resource'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetThirdPartyOptions
 * url: GetThirdPartyOptionsURL
 * method: GetThirdPartyOptions_TYPE
 * raw_url: GetThirdPartyOptions_RAW_URL
 */
export const GetThirdPartyOptions = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/options/thirdparty'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetThirdPartyOptions_RAW_URL = function() {
  return '/options/thirdparty'
}
export const GetThirdPartyOptions_TYPE = function() {
  return 'get'
}
export const GetThirdPartyOptionsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/options/thirdparty'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: PatchThirdPartyOptions
 * url: PatchThirdPartyOptionsURL
 * method: PatchThirdPartyOptions_TYPE
 * raw_url: PatchThirdPartyOptions_RAW_URL
 * @param model - 
 */
export const PatchThirdPartyOptions = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/options/thirdparty'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('patch', domain + path, body, queryParameters, form, config)
}
export const PatchThirdPartyOptions_RAW_URL = function() {
  return '/options/thirdparty'
}
export const PatchThirdPartyOptions_TYPE = function() {
  return 'patch'
}
export const PatchThirdPartyOptionsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/options/thirdparty'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetNetworkOptions
 * url: GetNetworkOptionsURL
 * method: GetNetworkOptions_TYPE
 * raw_url: GetNetworkOptions_RAW_URL
 */
export const GetNetworkOptions = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/options/network'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetNetworkOptions_RAW_URL = function() {
  return '/options/network'
}
export const GetNetworkOptions_TYPE = function() {
  return 'get'
}
export const GetNetworkOptionsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/options/network'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: PatchNetworkOptions
 * url: PatchNetworkOptionsURL
 * method: PatchNetworkOptions_TYPE
 * raw_url: PatchNetworkOptions_RAW_URL
 * @param model - 
 */
export const PatchNetworkOptions = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/options/network'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('patch', domain + path, body, queryParameters, form, config)
}
export const PatchNetworkOptions_RAW_URL = function() {
  return '/options/network'
}
export const PatchNetworkOptions_TYPE = function() {
  return 'patch'
}
export const PatchNetworkOptionsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/options/network'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: SearchPasswords
 * url: SearchPasswordsURL
 * method: SearchPasswords_TYPE
 * raw_url: SearchPasswords_RAW_URL
 * @param order - 
 * @param pageIndex - 
 * @param pageSize - 
 * @param skipCount - 
 */
export const SearchPasswords = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/password'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['order'] !== undefined) {
    queryParameters['order'] = parameters['order']
  }
  if (parameters['pageIndex'] !== undefined) {
    queryParameters['pageIndex'] = parameters['pageIndex']
  }
  if (parameters['pageSize'] !== undefined) {
    queryParameters['pageSize'] = parameters['pageSize']
  }
  if (parameters['skipCount'] !== undefined) {
    queryParameters['skipCount'] = parameters['skipCount']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const SearchPasswords_RAW_URL = function() {
  return '/password'
}
export const SearchPasswords_TYPE = function() {
  return 'get'
}
export const SearchPasswordsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/password'
  if (parameters['order'] !== undefined) {
    queryParameters['order'] = parameters['order']
  }
  if (parameters['pageIndex'] !== undefined) {
    queryParameters['pageIndex'] = parameters['pageIndex']
  }
  if (parameters['pageSize'] !== undefined) {
    queryParameters['pageSize'] = parameters['pageSize']
  }
  if (parameters['skipCount'] !== undefined) {
    queryParameters['skipCount'] = parameters['skipCount']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetAllPasswords
 * url: GetAllPasswordsURL
 * method: GetAllPasswords_TYPE
 * raw_url: GetAllPasswords_RAW_URL
 */
export const GetAllPasswords = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/password/all'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetAllPasswords_RAW_URL = function() {
  return '/password/all'
}
export const GetAllPasswords_TYPE = function() {
  return 'get'
}
export const GetAllPasswordsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/password/all'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: DeletePassword
 * url: DeletePasswordURL
 * method: DeletePassword_TYPE
 * raw_url: DeletePassword_RAW_URL
 * @param password - 
 */
export const DeletePassword = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/password/{password}'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{password}', `${parameters['password']}`)
  if (parameters['password'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: password'))
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('delete', domain + path, body, queryParameters, form, config)
}
export const DeletePassword_RAW_URL = function() {
  return '/password/{password}'
}
export const DeletePassword_TYPE = function() {
  return 'delete'
}
export const DeletePasswordURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/password/{password}'
  path = path.replace('{password}', `${parameters['password']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetPlaylist
 * url: GetPlaylistURL
 * method: GetPlaylist_TYPE
 * raw_url: GetPlaylist_RAW_URL
 * @param id - 
 */
export const GetPlaylist = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/playlist/{id}'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetPlaylist_RAW_URL = function() {
  return '/playlist/{id}'
}
export const GetPlaylist_TYPE = function() {
  return 'get'
}
export const GetPlaylistURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/playlist/{id}'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: DeletePlaylist
 * url: DeletePlaylistURL
 * method: DeletePlaylist_TYPE
 * raw_url: DeletePlaylist_RAW_URL
 * @param id - 
 */
export const DeletePlaylist = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/playlist/{id}'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('delete', domain + path, body, queryParameters, form, config)
}
export const DeletePlaylist_RAW_URL = function() {
  return '/playlist/{id}'
}
export const DeletePlaylist_TYPE = function() {
  return 'delete'
}
export const DeletePlaylistURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/playlist/{id}'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetAllPlaylists
 * url: GetAllPlaylistsURL
 * method: GetAllPlaylists_TYPE
 * raw_url: GetAllPlaylists_RAW_URL
 */
export const GetAllPlaylists = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/playlist'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetAllPlaylists_RAW_URL = function() {
  return '/playlist'
}
export const GetAllPlaylists_TYPE = function() {
  return 'get'
}
export const GetAllPlaylistsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/playlist'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: AddPlaylist
 * url: AddPlaylistURL
 * method: AddPlaylist_TYPE
 * raw_url: AddPlaylist_RAW_URL
 * @param model - 
 */
export const AddPlaylist = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/playlist'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('post', domain + path, body, queryParameters, form, config)
}
export const AddPlaylist_RAW_URL = function() {
  return '/playlist'
}
export const AddPlaylist_TYPE = function() {
  return 'post'
}
export const AddPlaylistURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/playlist'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: PutPlaylist
 * url: PutPlaylistURL
 * method: PutPlaylist_TYPE
 * raw_url: PutPlaylist_RAW_URL
 * @param model - 
 */
export const PutPlaylist = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/playlist'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('put', domain + path, body, queryParameters, form, config)
}
export const PutPlaylist_RAW_URL = function() {
  return '/playlist'
}
export const PutPlaylist_TYPE = function() {
  return 'put'
}
export const PutPlaylistURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/playlist'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetPlaylistFiles
 * url: GetPlaylistFilesURL
 * method: GetPlaylistFiles_TYPE
 * raw_url: GetPlaylistFiles_RAW_URL
 * @param id - 
 */
export const GetPlaylistFiles = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/playlist/{id}/files'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetPlaylistFiles_RAW_URL = function() {
  return '/playlist/{id}/files'
}
export const GetPlaylistFiles_TYPE = function() {
  return 'get'
}
export const GetPlaylistFilesURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/playlist/{id}/files'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: UpdatePublisher
 * url: UpdatePublisherURL
 * method: UpdatePublisher_TYPE
 * raw_url: UpdatePublisher_RAW_URL
 * @param id - 
 * @param model - 
 */
export const UpdatePublisher = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/publisher/{id}'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('put', domain + path, body, queryParameters, form, config)
}
export const UpdatePublisher_RAW_URL = function() {
  return '/publisher/{id}'
}
export const UpdatePublisher_TYPE = function() {
  return 'put'
}
export const UpdatePublisherURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/publisher/{id}'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: SearchResources
 * url: SearchResourcesURL
 * method: SearchResources_TYPE
 * raw_url: SearchResources_RAW_URL
 * @param model - 
 */
export const SearchResources = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/resource/search'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('post', domain + path, body, queryParameters, form, config)
}
export const SearchResources_RAW_URL = function() {
  return '/resource/search'
}
export const SearchResources_TYPE = function() {
  return 'post'
}
export const SearchResourcesURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/resource/search'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetResourcesByKeys
 * url: GetResourcesByKeysURL
 * method: GetResourcesByKeys_TYPE
 * raw_url: GetResourcesByKeys_RAW_URL
 * @param ids - 
 */
export const GetResourcesByKeys = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/resource/keys'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['ids'] !== undefined) {
    queryParameters['ids'] = parameters['ids']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetResourcesByKeys_RAW_URL = function() {
  return '/resource/keys'
}
export const GetResourcesByKeys_TYPE = function() {
  return 'get'
}
export const GetResourcesByKeysURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/resource/keys'
  if (parameters['ids'] !== undefined) {
    queryParameters['ids'] = parameters['ids']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: PatchResource
 * url: PatchResourceURL
 * method: PatchResource_TYPE
 * raw_url: PatchResource_RAW_URL
 * @param id - 
 * @param model - 
 */
export const PatchResource = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/resource/{id}'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('put', domain + path, body, queryParameters, form, config)
}
export const PatchResource_RAW_URL = function() {
  return '/resource/{id}'
}
export const PatchResource_TYPE = function() {
  return 'put'
}
export const PatchResourceURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/resource/{id}'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: RemoveResource
 * url: RemoveResourceURL
 * method: RemoveResource_TYPE
 * raw_url: RemoveResource_RAW_URL
 * @param id - 
 */
export const RemoveResource = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/resource/{id}'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('delete', domain + path, body, queryParameters, form, config)
}
export const RemoveResource_RAW_URL = function() {
  return '/resource/{id}'
}
export const RemoveResource_TYPE = function() {
  return 'delete'
}
export const RemoveResourceURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/resource/{id}'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: UpdateResourceTags
 * url: UpdateResourceTagsURL
 * method: UpdateResourceTags_TYPE
 * raw_url: UpdateResourceTags_RAW_URL
 * @param model - 
 */
export const UpdateResourceTags = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/resource/tag'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('put', domain + path, body, queryParameters, form, config)
}
export const UpdateResourceTags_RAW_URL = function() {
  return '/resource/tag'
}
export const UpdateResourceTags_TYPE = function() {
  return 'put'
}
export const UpdateResourceTagsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/resource/tag'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: OpenResourceDirectory
 * url: OpenResourceDirectoryURL
 * method: OpenResourceDirectory_TYPE
 * raw_url: OpenResourceDirectory_RAW_URL
 * @param id - 
 */
export const OpenResourceDirectory = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/resource/directory'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['id'] !== undefined) {
    queryParameters['id'] = parameters['id']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const OpenResourceDirectory_RAW_URL = function() {
  return '/resource/directory'
}
export const OpenResourceDirectory_TYPE = function() {
  return 'get'
}
export const OpenResourceDirectoryURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/resource/directory'
  if (parameters['id'] !== undefined) {
    queryParameters['id'] = parameters['id']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: UpdateResourceRawName
 * url: UpdateResourceRawNameURL
 * method: UpdateResourceRawName_TYPE
 * raw_url: UpdateResourceRawName_RAW_URL
 * @param id - 
 * @param model - 
 */
export const UpdateResourceRawName = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/resource/{id}/raw-name'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('delete', domain + path, body, queryParameters, form, config)
}
export const UpdateResourceRawName_RAW_URL = function() {
  return '/resource/{id}/raw-name'
}
export const UpdateResourceRawName_TYPE = function() {
  return 'delete'
}
export const UpdateResourceRawNameURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/resource/{id}/raw-name'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetResourceCover
 * url: GetResourceCoverURL
 * method: GetResourceCover_TYPE
 * raw_url: GetResourceCover_RAW_URL
 * @param id - 
 */
export const GetResourceCover = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/resource/{id}/cover'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetResourceCover_RAW_URL = function() {
  return '/resource/{id}/cover'
}
export const GetResourceCover_TYPE = function() {
  return 'get'
}
export const GetResourceCoverURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/resource/{id}/cover'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: SaveCover
 * url: SaveCoverURL
 * method: SaveCover_TYPE
 * raw_url: SaveCover_RAW_URL
 * @param id - 
 * @param model - 
 */
export const SaveCover = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/resource/{id}/cover'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('post', domain + path, body, queryParameters, form, config)
}
export const SaveCover_RAW_URL = function() {
  return '/resource/{id}/cover'
}
export const SaveCover_TYPE = function() {
  return 'post'
}
export const SaveCoverURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/resource/{id}/cover'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: RemoveCoverCache
 * url: RemoveCoverCacheURL
 * method: RemoveCoverCache_TYPE
 * raw_url: RemoveCoverCache_RAW_URL
 * @param id - 
 */
export const RemoveCoverCache = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/resource/{id}/cover/cache'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('delete', domain + path, body, queryParameters, form, config)
}
export const RemoveCoverCache_RAW_URL = function() {
  return '/resource/{id}/cover/cache'
}
export const RemoveCoverCache_TYPE = function() {
  return 'delete'
}
export const RemoveCoverCacheURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/resource/{id}/cover/cache'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetResourcePlayableFiles
 * url: GetResourcePlayableFilesURL
 * method: GetResourcePlayableFiles_TYPE
 * raw_url: GetResourcePlayableFiles_RAW_URL
 * @param id - 
 */
export const GetResourcePlayableFiles = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/resource/{id}/playable-files'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetResourcePlayableFiles_RAW_URL = function() {
  return '/resource/{id}/playable-files'
}
export const GetResourcePlayableFiles_TYPE = function() {
  return 'get'
}
export const GetResourcePlayableFilesURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/resource/{id}/playable-files'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetAllCustomPropertiesAndCandidates
 * url: GetAllCustomPropertiesAndCandidatesURL
 * method: GetAllCustomPropertiesAndCandidates_TYPE
 * raw_url: GetAllCustomPropertiesAndCandidates_RAW_URL
 */
export const GetAllCustomPropertiesAndCandidates = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/resource/custom-properties-and-candidates'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetAllCustomPropertiesAndCandidates_RAW_URL = function() {
  return '/resource/custom-properties-and-candidates'
}
export const GetAllCustomPropertiesAndCandidates_TYPE = function() {
  return 'get'
}
export const GetAllCustomPropertiesAndCandidatesURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/resource/custom-properties-and-candidates'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetAllReservedPropertiesAndCandidates
 * url: GetAllReservedPropertiesAndCandidatesURL
 * method: GetAllReservedPropertiesAndCandidates_TYPE
 * raw_url: GetAllReservedPropertiesAndCandidates_RAW_URL
 */
export const GetAllReservedPropertiesAndCandidates = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/resource/reserved-properties-and-candidates'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetAllReservedPropertiesAndCandidates_RAW_URL = function() {
  return '/resource/reserved-properties-and-candidates'
}
export const GetAllReservedPropertiesAndCandidates_TYPE = function() {
  return 'get'
}
export const GetAllReservedPropertiesAndCandidatesURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/resource/reserved-properties-and-candidates'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: RemoveResourceCustomProperty
 * url: RemoveResourceCustomPropertyURL
 * method: RemoveResourceCustomProperty_TYPE
 * raw_url: RemoveResourceCustomProperty_RAW_URL
 * @param id - 
 * @param propertyKey - 
 */
export const RemoveResourceCustomProperty = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/resource/resource/{id}/custom-property/{propertyKey}'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  path = path.replace('{propertyKey}', `${parameters['propertyKey']}`)
  if (parameters['propertyKey'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: propertyKey'))
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('delete', domain + path, body, queryParameters, form, config)
}
export const RemoveResourceCustomProperty_RAW_URL = function() {
  return '/resource/resource/{id}/custom-property/{propertyKey}'
}
export const RemoveResourceCustomProperty_TYPE = function() {
  return 'delete'
}
export const RemoveResourceCustomPropertyURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/resource/resource/{id}/custom-property/{propertyKey}'
  path = path.replace('{id}', `${parameters['id']}`)
  path = path.replace('{propertyKey}', `${parameters['propertyKey']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: CheckResourceExistence
 * url: CheckResourceExistenceURL
 * method: CheckResourceExistence_TYPE
 * raw_url: CheckResourceExistence_RAW_URL
 * @param name - 
 */
export const CheckResourceExistence = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/resource/existence'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['name'] !== undefined) {
    queryParameters['name'] = parameters['name']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const CheckResourceExistence_RAW_URL = function() {
  return '/resource/existence'
}
export const CheckResourceExistence_TYPE = function() {
  return 'get'
}
export const CheckResourceExistenceURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/resource/existence'
  if (parameters['name'] !== undefined) {
    queryParameters['name'] = parameters['name']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: MoveResources
 * url: MoveResourcesURL
 * method: MoveResources_TYPE
 * raw_url: MoveResources_RAW_URL
 * @param model - 
 */
export const MoveResources = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/resource/move'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('put', domain + path, body, queryParameters, form, config)
}
export const MoveResources_RAW_URL = function() {
  return '/resource/move'
}
export const MoveResources_TYPE = function() {
  return 'put'
}
export const MoveResourcesURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/resource/move'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: ClearResourceTask
 * url: ClearResourceTaskURL
 * method: ClearResourceTask_TYPE
 * raw_url: ClearResourceTask_RAW_URL
 * @param id - 
 */
export const ClearResourceTask = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/resource/{id}/task'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('delete', domain + path, body, queryParameters, form, config)
}
export const ClearResourceTask_RAW_URL = function() {
  return '/resource/{id}/task'
}
export const ClearResourceTask_TYPE = function() {
  return 'delete'
}
export const ClearResourceTaskURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/resource/{id}/task'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetFavoritesResourcesMappings
 * url: GetFavoritesResourcesMappingsURL
 * method: GetFavoritesResourcesMappings_TYPE
 * raw_url: GetFavoritesResourcesMappings_RAW_URL
 * @param ids - 
 */
export const GetFavoritesResourcesMappings = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/resource/favorites-mappings'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['ids'] !== undefined) {
    queryParameters['ids'] = parameters['ids']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetFavoritesResourcesMappings_RAW_URL = function() {
  return '/resource/favorites-mappings'
}
export const GetFavoritesResourcesMappings_TYPE = function() {
  return 'get'
}
export const GetFavoritesResourcesMappingsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/resource/favorites-mappings'
  if (parameters['ids'] !== undefined) {
    queryParameters['ids'] = parameters['ids']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: StartResourceNfoGenerationTask
 * url: StartResourceNfoGenerationTaskURL
 * method: StartResourceNfoGenerationTask_TYPE
 * raw_url: StartResourceNfoGenerationTask_RAW_URL
 */
export const StartResourceNfoGenerationTask = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/resource/nfo'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('post', domain + path, body, queryParameters, form, config)
}
export const StartResourceNfoGenerationTask_RAW_URL = function() {
  return '/resource/nfo'
}
export const StartResourceNfoGenerationTask_TYPE = function() {
  return 'post'
}
export const StartResourceNfoGenerationTaskURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/resource/nfo'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetResourceDataForPreviewer
 * url: GetResourceDataForPreviewerURL
 * method: GetResourceDataForPreviewer_TYPE
 * raw_url: GetResourceDataForPreviewer_RAW_URL
 * @param id - 
 */
export const GetResourceDataForPreviewer = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/resource/{id}/previewer'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetResourceDataForPreviewer_RAW_URL = function() {
  return '/resource/{id}/previewer'
}
export const GetResourceDataForPreviewer_TYPE = function() {
  return 'get'
}
export const GetResourceDataForPreviewerURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/resource/{id}/previewer'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetAllResourceCategories
 * url: GetAllResourceCategoriesURL
 * method: GetAllResourceCategories_TYPE
 * raw_url: GetAllResourceCategories_RAW_URL
 * @param additionalItems - 
 */
export const GetAllResourceCategories = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/resource-category'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['additionalItems'] !== undefined) {
    queryParameters['additionalItems'] = parameters['additionalItems']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetAllResourceCategories_RAW_URL = function() {
  return '/resource-category'
}
export const GetAllResourceCategories_TYPE = function() {
  return 'get'
}
export const GetAllResourceCategoriesURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/resource-category'
  if (parameters['additionalItems'] !== undefined) {
    queryParameters['additionalItems'] = parameters['additionalItems']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: AddResourceCategory
 * url: AddResourceCategoryURL
 * method: AddResourceCategory_TYPE
 * raw_url: AddResourceCategory_RAW_URL
 * @param model - 
 */
export const AddResourceCategory = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/resource-category'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('post', domain + path, body, queryParameters, form, config)
}
export const AddResourceCategory_RAW_URL = function() {
  return '/resource-category'
}
export const AddResourceCategory_TYPE = function() {
  return 'post'
}
export const AddResourceCategoryURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/resource-category'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: UpdateResourceCategory
 * url: UpdateResourceCategoryURL
 * method: UpdateResourceCategory_TYPE
 * raw_url: UpdateResourceCategory_RAW_URL
 * @param id - 
 * @param model - 
 */
export const UpdateResourceCategory = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/resource-category/{id}'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('put', domain + path, body, queryParameters, form, config)
}
export const UpdateResourceCategory_RAW_URL = function() {
  return '/resource-category/{id}'
}
export const UpdateResourceCategory_TYPE = function() {
  return 'put'
}
export const UpdateResourceCategoryURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/resource-category/{id}'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: RemoveResourceCategory
 * url: RemoveResourceCategoryURL
 * method: RemoveResourceCategory_TYPE
 * raw_url: RemoveResourceCategory_RAW_URL
 * @param id - 
 */
export const RemoveResourceCategory = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/resource-category/{id}'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('delete', domain + path, body, queryParameters, form, config)
}
export const RemoveResourceCategory_RAW_URL = function() {
  return '/resource-category/{id}'
}
export const RemoveResourceCategory_TYPE = function() {
  return 'delete'
}
export const RemoveResourceCategoryURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/resource-category/{id}'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: ConfigureResourceCategoryComponents
 * url: ConfigureResourceCategoryComponentsURL
 * method: ConfigureResourceCategoryComponents_TYPE
 * raw_url: ConfigureResourceCategoryComponents_RAW_URL
 * @param id - 
 * @param model - 
 */
export const ConfigureResourceCategoryComponents = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/resource-category/{id}/component'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('put', domain + path, body, queryParameters, form, config)
}
export const ConfigureResourceCategoryComponents_RAW_URL = function() {
  return '/resource-category/{id}/component'
}
export const ConfigureResourceCategoryComponents_TYPE = function() {
  return 'put'
}
export const ConfigureResourceCategoryComponentsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/resource-category/{id}/component'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: SortCategories
 * url: SortCategoriesURL
 * method: SortCategories_TYPE
 * raw_url: SortCategories_RAW_URL
 * @param model - 
 */
export const SortCategories = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/resource-category/orders'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('put', domain + path, body, queryParameters, form, config)
}
export const SortCategories_RAW_URL = function() {
  return '/resource-category/orders'
}
export const SortCategories_TYPE = function() {
  return 'put'
}
export const SortCategoriesURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/resource-category/orders'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: SaveDataFromSetupWizard
 * url: SaveDataFromSetupWizardURL
 * method: SaveDataFromSetupWizard_TYPE
 * raw_url: SaveDataFromSetupWizard_RAW_URL
 * @param model - 
 */
export const SaveDataFromSetupWizard = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/resource-category/setup-wizard'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('post', domain + path, body, queryParameters, form, config)
}
export const SaveDataFromSetupWizard_RAW_URL = function() {
  return '/resource-category/setup-wizard'
}
export const SaveDataFromSetupWizard_TYPE = function() {
  return 'post'
}
export const SaveDataFromSetupWizardURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/resource-category/setup-wizard'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetAllSpecialText
 * url: GetAllSpecialTextURL
 * method: GetAllSpecialText_TYPE
 * raw_url: GetAllSpecialText_RAW_URL
 */
export const GetAllSpecialText = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/special-text'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetAllSpecialText_RAW_URL = function() {
  return '/special-text'
}
export const GetAllSpecialText_TYPE = function() {
  return 'get'
}
export const GetAllSpecialTextURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/special-text'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: CreateSpecialText
 * url: CreateSpecialTextURL
 * method: CreateSpecialText_TYPE
 * raw_url: CreateSpecialText_RAW_URL
 * @param model - 
 */
export const CreateSpecialText = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/special-text'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('post', domain + path, body, queryParameters, form, config)
}
export const CreateSpecialText_RAW_URL = function() {
  return '/special-text'
}
export const CreateSpecialText_TYPE = function() {
  return 'post'
}
export const CreateSpecialTextURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/special-text'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: DeleteSpecialText
 * url: DeleteSpecialTextURL
 * method: DeleteSpecialText_TYPE
 * raw_url: DeleteSpecialText_RAW_URL
 * @param id - 
 */
export const DeleteSpecialText = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/special-text/{id}'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('delete', domain + path, body, queryParameters, form, config)
}
export const DeleteSpecialText_RAW_URL = function() {
  return '/special-text/{id}'
}
export const DeleteSpecialText_TYPE = function() {
  return 'delete'
}
export const DeleteSpecialTextURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/special-text/{id}'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: UpdateSpecialText
 * url: UpdateSpecialTextURL
 * method: UpdateSpecialText_TYPE
 * raw_url: UpdateSpecialText_RAW_URL
 * @param id - 
 * @param model - 
 */
export const UpdateSpecialText = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/special-text/{id}'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('put', domain + path, body, queryParameters, form, config)
}
export const UpdateSpecialText_RAW_URL = function() {
  return '/special-text/{id}'
}
export const UpdateSpecialText_TYPE = function() {
  return 'put'
}
export const UpdateSpecialTextURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/special-text/{id}'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: AddSpecialTextPrefabs
 * url: AddSpecialTextPrefabsURL
 * method: AddSpecialTextPrefabs_TYPE
 * raw_url: AddSpecialTextPrefabs_RAW_URL
 */
export const AddSpecialTextPrefabs = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/special-text/prefabs'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('post', domain + path, body, queryParameters, form, config)
}
export const AddSpecialTextPrefabs_RAW_URL = function() {
  return '/special-text/prefabs'
}
export const AddSpecialTextPrefabs_TYPE = function() {
  return 'post'
}
export const AddSpecialTextPrefabsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/special-text/prefabs'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: PretreatText
 * url: PretreatTextURL
 * method: PretreatText_TYPE
 * raw_url: PretreatText_RAW_URL
 * @param text - 
 */
export const PretreatText = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/special-text/pretreatment'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['text'] !== undefined) {
    queryParameters['text'] = parameters['text']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('post', domain + path, body, queryParameters, form, config)
}
export const PretreatText_RAW_URL = function() {
  return '/special-text/pretreatment'
}
export const PretreatText_TYPE = function() {
  return 'post'
}
export const PretreatTextURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/special-text/pretreatment'
  if (parameters['text'] !== undefined) {
    queryParameters['text'] = parameters['text']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetAllTags
 * url: GetAllTagsURL
 * method: GetAllTags_TYPE
 * raw_url: GetAllTags_RAW_URL
 * @param additionalItems - 
 */
export const GetAllTags = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/tag'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['additionalItems'] !== undefined) {
    queryParameters['additionalItems'] = parameters['additionalItems']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetAllTags_RAW_URL = function() {
  return '/tag'
}
export const GetAllTags_TYPE = function() {
  return 'get'
}
export const GetAllTagsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/tag'
  if (parameters['additionalItems'] !== undefined) {
    queryParameters['additionalItems'] = parameters['additionalItems']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: AddTags
 * url: AddTagsURL
 * method: AddTags_TYPE
 * raw_url: AddTags_RAW_URL
 * @param model - 
 */
export const AddTags = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/tag'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('post', domain + path, body, queryParameters, form, config)
}
export const AddTags_RAW_URL = function() {
  return '/tag'
}
export const AddTags_TYPE = function() {
  return 'post'
}
export const AddTagsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/tag'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: UpdateTagName
 * url: UpdateTagNameURL
 * method: UpdateTagName_TYPE
 * raw_url: UpdateTagName_RAW_URL
 * @param id - 
 * @param model - 
 */
export const UpdateTagName = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/tag/{id}/name'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('put', domain + path, body, queryParameters, form, config)
}
export const UpdateTagName_RAW_URL = function() {
  return '/tag/{id}/name'
}
export const UpdateTagName_TYPE = function() {
  return 'put'
}
export const UpdateTagNameURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/tag/{id}/name'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: UpdateTag
 * url: UpdateTagURL
 * method: UpdateTag_TYPE
 * raw_url: UpdateTag_RAW_URL
 * @param id - 
 * @param model - 
 */
export const UpdateTag = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/tag/{id}'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('patch', domain + path, body, queryParameters, form, config)
}
export const UpdateTag_RAW_URL = function() {
  return '/tag/{id}'
}
export const UpdateTag_TYPE = function() {
  return 'patch'
}
export const UpdateTagURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/tag/{id}'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: RemoveTag
 * url: RemoveTagURL
 * method: RemoveTag_TYPE
 * raw_url: RemoveTag_RAW_URL
 * @param id - 
 */
export const RemoveTag = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/tag/{id}'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('delete', domain + path, body, queryParameters, form, config)
}
export const RemoveTag_RAW_URL = function() {
  return '/tag/{id}'
}
export const RemoveTag_TYPE = function() {
  return 'delete'
}
export const RemoveTagURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/tag/{id}'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: MoveTag
 * url: MoveTagURL
 * method: MoveTag_TYPE
 * raw_url: MoveTag_RAW_URL
 * @param id - 
 * @param model - 
 */
export const MoveTag = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/tag/{id}/move'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('put', domain + path, body, queryParameters, form, config)
}
export const MoveTag_RAW_URL = function() {
  return '/tag/{id}/move'
}
export const MoveTag_TYPE = function() {
  return 'put'
}
export const MoveTagURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/tag/{id}/move'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: BulkDeleteTags
 * url: BulkDeleteTagsURL
 * method: BulkDeleteTags_TYPE
 * raw_url: BulkDeleteTags_RAW_URL
 * @param model - 
 */
export const BulkDeleteTags = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/tag/bulk'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('delete', domain + path, body, queryParameters, form, config)
}
export const BulkDeleteTags_RAW_URL = function() {
  return '/tag/bulk'
}
export const BulkDeleteTags_TYPE = function() {
  return 'delete'
}
export const BulkDeleteTagsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/tag/bulk'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetAllTagGroups
 * url: GetAllTagGroupsURL
 * method: GetAllTagGroups_TYPE
 * raw_url: GetAllTagGroups_RAW_URL
 * @param additionalItems - 
 */
export const GetAllTagGroups = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/TagGroup'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['additionalItems'] !== undefined) {
    queryParameters['additionalItems'] = parameters['additionalItems']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetAllTagGroups_RAW_URL = function() {
  return '/TagGroup'
}
export const GetAllTagGroups_TYPE = function() {
  return 'get'
}
export const GetAllTagGroupsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/TagGroup'
  if (parameters['additionalItems'] !== undefined) {
    queryParameters['additionalItems'] = parameters['additionalItems']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: AddTagGroups
 * url: AddTagGroupsURL
 * method: AddTagGroups_TYPE
 * raw_url: AddTagGroups_RAW_URL
 * @param model - 
 */
export const AddTagGroups = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/TagGroup'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('post', domain + path, body, queryParameters, form, config)
}
export const AddTagGroups_RAW_URL = function() {
  return '/TagGroup'
}
export const AddTagGroups_TYPE = function() {
  return 'post'
}
export const AddTagGroupsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/TagGroup'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: UpdateTagGroup
 * url: UpdateTagGroupURL
 * method: UpdateTagGroup_TYPE
 * raw_url: UpdateTagGroup_RAW_URL
 * @param id - 
 * @param model - 
 */
export const UpdateTagGroup = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/TagGroup/{id}'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('put', domain + path, body, queryParameters, form, config)
}
export const UpdateTagGroup_RAW_URL = function() {
  return '/TagGroup/{id}'
}
export const UpdateTagGroup_TYPE = function() {
  return 'put'
}
export const UpdateTagGroupURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/TagGroup/{id}'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: RemoveTagGroup
 * url: RemoveTagGroupURL
 * method: RemoveTagGroup_TYPE
 * raw_url: RemoveTagGroup_RAW_URL
 * @param id - 
 */
export const RemoveTagGroup = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/TagGroup/{id}'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('delete', domain + path, body, queryParameters, form, config)
}
export const RemoveTagGroup_RAW_URL = function() {
  return '/TagGroup/{id}'
}
export const RemoveTagGroup_TYPE = function() {
  return 'delete'
}
export const RemoveTagGroupURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/TagGroup/{id}'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: SortTagGroups
 * url: SortTagGroupsURL
 * method: SortTagGroups_TYPE
 * raw_url: SortTagGroups_RAW_URL
 * @param model - 
 */
export const SortTagGroups = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/TagGroup/orders'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('put', domain + path, body, queryParameters, form, config)
}
export const SortTagGroups_RAW_URL = function() {
  return '/TagGroup/orders'
}
export const SortTagGroups_TYPE = function() {
  return 'put'
}
export const SortTagGroupsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/TagGroup/orders'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetAllThirdPartyRequestStatistics
 * url: GetAllThirdPartyRequestStatisticsURL
 * method: GetAllThirdPartyRequestStatistics_TYPE
 * raw_url: GetAllThirdPartyRequestStatistics_RAW_URL
 */
export const GetAllThirdPartyRequestStatistics = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/third-party/request-statistics'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetAllThirdPartyRequestStatistics_RAW_URL = function() {
  return '/third-party/request-statistics'
}
export const GetAllThirdPartyRequestStatistics_TYPE = function() {
  return 'get'
}
export const GetAllThirdPartyRequestStatisticsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/third-party/request-statistics'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: ExtraSubdirectories
 * url: ExtraSubdirectoriesURL
 * method: ExtraSubdirectories_TYPE
 * raw_url: ExtraSubdirectories_RAW_URL
 * @param model - 
 */
export const ExtraSubdirectories = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/tool/extra-subdirectories'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('post', domain + path, body, queryParameters, form, config)
}
export const ExtraSubdirectories_RAW_URL = function() {
  return '/tool/extra-subdirectories'
}
export const ExtraSubdirectories_TYPE = function() {
  return 'post'
}
export const ExtraSubdirectoriesURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/tool/extra-subdirectories'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: OpenFileOrDirectory
 * url: OpenFileOrDirectoryURL
 * method: OpenFileOrDirectory_TYPE
 * raw_url: OpenFileOrDirectory_RAW_URL
 * @param path - 
 * @param openInDirectory - 
 */
export const OpenFileOrDirectory = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/tool/open'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['path'] !== undefined) {
    queryParameters['path'] = parameters['path']
  }
  if (parameters['openInDirectory'] !== undefined) {
    queryParameters['openInDirectory'] = parameters['openInDirectory']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const OpenFileOrDirectory_RAW_URL = function() {
  return '/tool/open'
}
export const OpenFileOrDirectory_TYPE = function() {
  return 'get'
}
export const OpenFileOrDirectoryURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/tool/open'
  if (parameters['path'] !== undefined) {
    queryParameters['path'] = parameters['path']
  }
  if (parameters['openInDirectory'] !== undefined) {
    queryParameters['openInDirectory'] = parameters['openInDirectory']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: RemoveRelayDirectories
 * url: RemoveRelayDirectoriesURL
 * method: RemoveRelayDirectories_TYPE
 * raw_url: RemoveRelayDirectories_RAW_URL
 * @param root - 
 */
export const RemoveRelayDirectories = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/tool/remove-relay-directories'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['root'] !== undefined) {
    queryParameters['root'] = parameters['root']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('post', domain + path, body, queryParameters, form, config)
}
export const RemoveRelayDirectories_RAW_URL = function() {
  return '/tool/remove-relay-directories'
}
export const RemoveRelayDirectories_TYPE = function() {
  return 'post'
}
export const RemoveRelayDirectoriesURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/tool/remove-relay-directories'
  if (parameters['root'] !== undefined) {
    queryParameters['root'] = parameters['root']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GroupFilesToDirectories
 * url: GroupFilesToDirectoriesURL
 * method: GroupFilesToDirectories_TYPE
 * raw_url: GroupFilesToDirectories_RAW_URL
 * @param root - 
 */
export const GroupFilesToDirectories = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/tool/group-files-into-directories'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['root'] !== undefined) {
    queryParameters['root'] = parameters['root']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('post', domain + path, body, queryParameters, form, config)
}
export const GroupFilesToDirectories_RAW_URL = function() {
  return '/tool/group-files-into-directories'
}
export const GroupFilesToDirectories_TYPE = function() {
  return 'post'
}
export const GroupFilesToDirectoriesURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/tool/group-files-into-directories'
  if (parameters['root'] !== undefined) {
    queryParameters['root'] = parameters['root']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: StartExtractEverything
 * url: StartExtractEverythingURL
 * method: StartExtractEverything_TYPE
 * raw_url: StartExtractEverything_RAW_URL
 * @param root - 
 */
export const StartExtractEverything = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/tool/everything-extraction'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['root'] !== undefined) {
    queryParameters['root'] = parameters['root']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('post', domain + path, body, queryParameters, form, config)
}
export const StartExtractEverything_RAW_URL = function() {
  return '/tool/everything-extraction'
}
export const StartExtractEverything_TYPE = function() {
  return 'post'
}
export const StartExtractEverythingURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/tool/everything-extraction'
  if (parameters['root'] !== undefined) {
    queryParameters['root'] = parameters['root']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: StopExtractingEverything
 * url: StopExtractingEverythingURL
 * method: StopExtractingEverything_TYPE
 * raw_url: StopExtractingEverything_RAW_URL
 */
export const StopExtractingEverything = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/tool/everything-extraction'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('delete', domain + path, body, queryParameters, form, config)
}
export const StopExtractingEverything_RAW_URL = function() {
  return '/tool/everything-extraction'
}
export const StopExtractingEverything_TYPE = function() {
  return 'delete'
}
export const StopExtractingEverythingURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/tool/everything-extraction'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetEverythingExtractionStatus
 * url: GetEverythingExtractionStatusURL
 * method: GetEverythingExtractionStatus_TYPE
 * raw_url: GetEverythingExtractionStatus_RAW_URL
 */
export const GetEverythingExtractionStatus = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/tool/everything-extraction/status'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetEverythingExtractionStatus_RAW_URL = function() {
  return '/tool/everything-extraction/status'
}
export const GetEverythingExtractionStatus_TYPE = function() {
  return 'get'
}
export const GetEverythingExtractionStatusURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/tool/everything-extraction/status'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: PlayResourceFile
 * url: PlayResourceFileURL
 * method: PlayResourceFile_TYPE
 * raw_url: PlayResourceFile_RAW_URL
 * @param id - 
 * @param file - 
 */
export const PlayResourceFile = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/tool/{id}/play'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters['file'] !== undefined) {
    queryParameters['file'] = parameters['file']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const PlayResourceFile_RAW_URL = function() {
  return '/tool/{id}/play'
}
export const PlayResourceFile_TYPE = function() {
  return 'get'
}
export const PlayResourceFileURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/tool/{id}/play'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['file'] !== undefined) {
    queryParameters['file'] = parameters['file']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * Test
 * request: getToolTest
 * url: getToolTestURL
 * method: getToolTest_TYPE
 * raw_url: getToolTest_RAW_URL
 */
export const getToolTest = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/tool/test'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const getToolTest_RAW_URL = function() {
  return '/tool/test'
}
export const getToolTest_TYPE = function() {
  return 'get'
}
export const getToolTestURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/tool/test'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: ValidateCookie
 * url: ValidateCookieURL
 * method: ValidateCookie_TYPE
 * raw_url: ValidateCookie_RAW_URL
 * @param target - 
 * @param cookie - 
 */
export const ValidateCookie = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/tool/cookie-validation'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['target'] !== undefined) {
    queryParameters['target'] = parameters['target']
  }
  if (parameters['cookie'] !== undefined) {
    queryParameters['cookie'] = parameters['cookie']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const ValidateCookie_RAW_URL = function() {
  return '/tool/cookie-validation'
}
export const ValidateCookie_TYPE = function() {
  return 'get'
}
export const ValidateCookieURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/tool/cookie-validation'
  if (parameters['target'] !== undefined) {
    queryParameters['target'] = parameters['target']
  }
  if (parameters['cookie'] !== undefined) {
    queryParameters['cookie'] = parameters['cookie']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetUpdaterUpdaterState
 * url: GetUpdaterUpdaterStateURL
 * method: GetUpdaterUpdaterState_TYPE
 * raw_url: GetUpdaterUpdaterState_RAW_URL
 */
export const GetUpdaterUpdaterState = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/updater/updater/state'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetUpdaterUpdaterState_RAW_URL = function() {
  return '/updater/updater/state'
}
export const GetUpdaterUpdaterState_TYPE = function() {
  return 'get'
}
export const GetUpdaterUpdaterStateURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/updater/updater/state'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: StartUpdatingUpdater
 * url: StartUpdatingUpdaterURL
 * method: StartUpdatingUpdater_TYPE
 * raw_url: StartUpdatingUpdater_RAW_URL
 */
export const StartUpdatingUpdater = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/updater/updater/update'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const StartUpdatingUpdater_RAW_URL = function() {
  return '/updater/updater/update'
}
export const StartUpdatingUpdater_TYPE = function() {
  return 'get'
}
export const StartUpdatingUpdaterURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/updater/updater/update'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetNewAppVersion
 * url: GetNewAppVersionURL
 * method: GetNewAppVersion_TYPE
 * raw_url: GetNewAppVersion_RAW_URL
 */
export const GetNewAppVersion = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/updater/app/new-version'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetNewAppVersion_RAW_URL = function() {
  return '/updater/app/new-version'
}
export const GetNewAppVersion_TYPE = function() {
  return 'get'
}
export const GetNewAppVersionURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/updater/app/new-version'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetAppUpdaterState
 * url: GetAppUpdaterStateURL
 * method: GetAppUpdaterState_TYPE
 * raw_url: GetAppUpdaterState_RAW_URL
 */
export const GetAppUpdaterState = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/updater/app/state'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetAppUpdaterState_RAW_URL = function() {
  return '/updater/app/state'
}
export const GetAppUpdaterState_TYPE = function() {
  return 'get'
}
export const GetAppUpdaterStateURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/updater/app/state'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: StartUpdatingApp
 * url: StartUpdatingAppURL
 * method: StartUpdatingApp_TYPE
 * raw_url: StartUpdatingApp_RAW_URL
 */
export const StartUpdatingApp = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/updater/app/update'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('post', domain + path, body, queryParameters, form, config)
}
export const StartUpdatingApp_RAW_URL = function() {
  return '/updater/app/update'
}
export const StartUpdatingApp_TYPE = function() {
  return 'post'
}
export const StartUpdatingAppURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/updater/app/update'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: StopUpdatingApp
 * url: StopUpdatingAppURL
 * method: StopUpdatingApp_TYPE
 * raw_url: StopUpdatingApp_RAW_URL
 */
export const StopUpdatingApp = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/updater/app/update'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('delete', domain + path, body, queryParameters, form, config)
}
export const StopUpdatingApp_RAW_URL = function() {
  return '/updater/app/update'
}
export const StopUpdatingApp_TYPE = function() {
  return 'delete'
}
export const StopUpdatingAppURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/updater/app/update'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: RestartAndUpdateApp
 * url: RestartAndUpdateAppURL
 * method: RestartAndUpdateApp_TYPE
 * raw_url: RestartAndUpdateApp_RAW_URL
 */
export const RestartAndUpdateApp = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/updater/app/restart'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('post', domain + path, body, queryParameters, form, config)
}
export const RestartAndUpdateApp_RAW_URL = function() {
  return '/updater/app/restart'
}
export const RestartAndUpdateApp_TYPE = function() {
  return 'post'
}
export const RestartAndUpdateAppURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/updater/app/restart'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}