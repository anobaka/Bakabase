// ==UserScript==
// @name         Bakabase 集成脚本
// @namespace    http://tampermonkey.net/
// @version      1.0.0
// @description  Bakabase 集成脚本
// @author       Bakabase
// @match        https://exhentai.org/*
// @grant        GM_xmlhttpRequest
// @grant        GM_getValue
// @grant        GM_setValue
// @grant        GM_getResourceText
// @grant        GM_addStyle
// @run-at       document-end
// @connect      localhost
// @connect      127.0.0.1
// @resource     IMPORTED_CSS https://cdn.jsdelivr.net/npm/toastify-js/src/toastify.min.css
// @require      https://cdn.jsdelivr.net/npm/toastify-js
// ==/UserScript==

(function() {
    'use strict';

    // Load Toastify CSS
    const my_css = GM_getResourceText("IMPORTED_CSS");
    GM_addStyle(my_css);

    // ==================== 使用说明 ====================
    // 
    // 标记显示规则：
    // 1. 未看过：不显示任何标记
    // 2. 已看过（无更新）：显示绿色 "✓" 标记，半透明，不影响阅读
    // 3. 有更新：显示橙色 "更新" 标记，稍微醒目一些
    //
    // 判断"有更新"的逻辑：
    // - 内容有 updatedAt 时间
    // - 且这个时间比上次查看时记录的 updatedAt 要晚
    // - 后端 API 会负责比较并返回 hasUpdate 标志
    //
    // 工作流程：
    // 1. 页面加载时，获取所有内容容器元素
    // 2. 从每个容器中提取内容 ID 和更新时间
    // 3. 查询这些 ID 的状态（已看过？有更新？）
    // 4. 用户滚动时，检查视口中的内容
    // 5. 自动标记视口中可见但未标记的内容
    // 6. 渲染标记到页面上
    //
    // ==================== 配置部分 ====================

    // API 基础地址（需要根据实际部署修改）
    const API_BASE_URL = GM_getValue('api_base_url', '{appEndpoint}');

    console.log('[Bakabase] API_BASE_URL:', API_BASE_URL);

    // 网站配置
    const SITE_CONFIGS = {
        'exhentai': {
            key: 'exhentai',
            domains: ['exhentai.org'],

            // 从 URL 提取 filter（例如从搜索关键词、标签等）
            extractFilter: (url) => {
                return null;
            },

            // 从 document 获取全部 content 的 container 元素
            findContents: (document) => {
                // 返回所有内容容器元素的数组
                return Array.from(document.querySelectorAll('.itg.gld .gl1t'));
            },

            // 从每个 element 中提取 id 和 updateTime
            // 返回 { id: string, updateTime: Date | null }
            extractContentInfo: (element) => {
                // 提取内容 ID
                const link = element.querySelector('.gl3t a');
                if (!link) {
                    return { id: null, updateTime: null };
                }
                
                // https://exhentai.org/g/3662642/0348cc9b33/
                const url = link.href;
                const u = new URL(url);
                // g后面那段
                const galleryId = u.pathname.split('/g/')[1]?.split('/')[0] || '';
                
                if (!galleryId) {
                    return { id: null, updateTime: null };
                }

                // 提取更新时间
                const dtId = `posted_${galleryId}`;
                const timeElement = document.getElementById(dtId);
                let updateTime = null;
                
                if (timeElement) {
                    const timeText = timeElement.textContent;
                    // timeText 是 UTC 时间（格式：YYYY-MM-DD HH:MM，无时区标识符），需要转换为当前时区
                    let utcTimeString = timeText.trim();
                    
                    // 将空格替换为 T（ISO 格式）
                    utcTimeString = utcTimeString.replace(' ', 'T');
                    
                    // 添加 Z 表示这是 UTC 时间
                    utcTimeString = utcTimeString + ':00Z';
                    
                    // 创建 Date 对象
                    // Date 对象在解析带 'Z' 的字符串时，会将其解析为 UTC 时间，然后自动转换为本地时区
                    // 例如：UTC 09:29 + 8小时 = 本地 17:29
                    updateTime = new Date(utcTimeString);
                }

                return { id: galleryId, updateTime: updateTime };
            },

            // 标记为已看过的事件
            onMarkViewed: () => {
                // 滚动时自动标记视口中可见的内容
                let markTimeout;
                window.addEventListener('scroll', () => {
                    // 使用防抖，避免频繁标记
                    clearTimeout(markTimeout);
                    markTimeout = setTimeout(() => {
                        markVisibleContentAsViewed();
                    }, 500);
                });
                
                // 页面加载后也检查一次
                setTimeout(() => markVisibleContentAsViewed(), 1000);
            },

            // 自定义渲染标记
            renderMarker: (element, status) => {
                // element: 内容对应的 DOM 元素
                // status: { isViewed, hasUpdate, viewedAt, updatedAt }

                // 移除旧标记、遮罩和下载按钮
                const oldMarker = element.querySelector('.bakabase-marker');
                if (oldMarker) {
                    oldMarker.remove();
                }
                const oldOverlay = element.querySelector('.bakabase-viewed-overlay');
                if (oldOverlay) {
                    oldOverlay.remove();
                }
                const oldDownloadBtn = element.querySelector('.bakabase-download-btn');
                if (oldDownloadBtn) {
                    oldDownloadBtn.remove();
                }

                // 确保父元素有 position: relative
                if (getComputedStyle(element).position === 'static') {
                    element.style.position = 'relative';
                }

                // 如果已看过但没有更新，添加弱化效果
                if (status.isViewed && !status.hasUpdate) {
                    const overlay = document.createElement('div');
                    overlay.className = 'bakabase-viewed-overlay';
                    overlay.style.cssText = `
                        position: absolute;
                        top: 0;
                        left: 0;
                        right: 0;
                        bottom: 0;
                        background-color: rgba(255, 255, 255, 0.1);
                        z-index: 99;
                        pointer-events: none;
                        transition: opacity 0.2s;
                    `;
                    element.appendChild(overlay);

                    // 鼠标悬停时减弱遮罩效果，方便查看
                    element.addEventListener('mouseenter', () => {
                        overlay.style.opacity = '0.3';
                    });
                    element.addEventListener('mouseleave', () => {
                        overlay.style.opacity = '1';
                    });
                }

                // 添加下载按钮（始终显示）
                const downloadBtn = document.createElement('div');
                downloadBtn.className = 'bakabase-download-btn';
                downloadBtn.style.cssText = `
                    position: absolute;
                    bottom: 2px;
                    right: 2px;
                    padding: 4px 8px;
                    border-radius: 3px;
                    font-size: 12px;
                    font-weight: bold;
                    z-index: 101;
                    background-color: #2196F3;
                    color: #fff;
                    cursor: pointer;
                    opacity: 0.8;
                    transition: opacity 0.2s, transform 0.2s;
                    pointer-events: auto;
                `;
                downloadBtn.textContent = '下载';

                downloadBtn.addEventListener('mouseenter', () => {
                    downloadBtn.style.opacity = '1';
                    downloadBtn.style.transform = 'scale(1.05)';
                });
                downloadBtn.addEventListener('mouseleave', () => {
                    downloadBtn.style.opacity = '0.8';
                    downloadBtn.style.transform = 'scale(1)';
                });

                // 点击下载按钮
                downloadBtn.addEventListener('click', (e) => {
                    e.preventDefault();
                    e.stopPropagation();

                    // 获取画廊链接
                    const link = element.querySelector('.gl3t a');
                    if (!link) {
                        alert('无法找到下载链接');
                        return;
                    }

                    const url = link.href;

                    // 发送下载请求
                    GM_xmlhttpRequest({
                        method: 'POST',
                        url: `${API_BASE_URL}/download-task/exhentai`,
                        headers: {
                            'Content-Type': 'application/json'
                        },
                        data: JSON.stringify({
                            type: 1, // ExHentaiDownloadTaskType.SingleWork
                            link: url
                        }),
                        onload: function(response) {
                            if (response.status === 200) {
                                const result = JSON.parse(response.responseText);
                                if (result.code) {
                                    alert(result.message);
                                    return;
                                }
                                Toastify({
                                    text: "已添加到下载队列",
                                    duration: 3000,
                                    style: {
                                        width: '200px',
                                        height: '40px',
                                        'font-size': '16px',
                                        background: "linear-gradient(to right, #00b09b, #96c93d)"
                                    },
                                }).showToast();
                            } else {
                                alert('下载请求失败');
                            }
                        },
                        onerror: function(error) {
                            alert('下载请求失败');
                            console.error('[Bakabase] 下载请求失败:', error);
                        }
                    });
                });

                element.appendChild(downloadBtn);

                // 如果已看过，添加状态标记
                if (!status.isViewed) {
                    return;
                }

                // 创建状态标记元素
                const marker = document.createElement('div');
                marker.className = 'bakabase-marker';

                // 基础样式（更小巧，不影响主内容）
                marker.style.cssText = `
                    position: absolute;
                    top: 2px;
                    right: 2px;
                    padding: 2px 6px;
                    border-radius: 3px;
                    font-size: 10px;
                    font-weight: normal;
                    z-index: 100;
                    opacity: 0.7;
                    transition: opacity 0.2s;
                    pointer-events: none;
                `;

                // 根据状态设置样式
                if (status.hasUpdate) {
                    // 有更新：橙色背景，更醒目
                    marker.textContent = '更新';
                    marker.style.backgroundColor = '#ff9800';
                    marker.style.color = '#fff';
                    marker.style.opacity = '0.85';
                } else {
                    // 已看过：绿色背景，更低调
                    marker.textContent = '✓';
                    marker.style.backgroundColor = '#4caf50';
                    marker.style.color = '#fff';
                    marker.style.opacity = '0.6';
                }

                // 鼠标悬停时稍微提高不透明度，便于查看
                element.addEventListener('mouseenter', () => {
                    marker.style.opacity = '1';
                });
                element.addEventListener('mouseleave', () => {
                    marker.style.opacity = status.hasUpdate ? '0.85' : '0.6';
                });

                element.appendChild(marker);
            }
        },

        // 可以继续添加其他网站的配置...
    };

    // ==================== 核心逻辑 ====================

    let currentSiteConfig = null;
    let currentContentIds = [];
    let contentStatusMap = new Map();

    // 初始化
    function init() {
        // 检测当前网站
        const hostname = window.location.hostname;
        for (const [key, config] of Object.entries(SITE_CONFIGS)) {
            if (config.domains.some(domain => hostname.includes(domain))) {
                currentSiteConfig = config;
                console.log(`[Bakabase] 检测到网站: ${config.key}`);
                break;
            }
        }

        if (!currentSiteConfig) {
            return; // 不支持的网站
        }

        // 启动追踪
        startTracking();
    }

    // 启动追踪
    function startTracking() {
        // 提取当前页面的内容
        extractAndQueryContent();

        // 设置标记已看过的事件
        if (currentSiteConfig.onMarkViewed) {
            currentSiteConfig.onMarkViewed();
        }

        // 监听滚动事件
        let scrollTimeout;
        window.addEventListener('scroll', () => {
            // 使用防抖，避免频繁触发
            clearTimeout(scrollTimeout);
            scrollTimeout = setTimeout(() => {
                extractAndQueryContent();
            }, 300);
        });
    }

    // 提取并查询内容状态
    function extractAndQueryContent() {
        if (!currentSiteConfig) return;
        if (!currentSiteConfig.findContents || !currentSiteConfig.extractContentInfo) {
            console.warn('[Bakabase] 网站配置未定义 findContents 或 extractContentInfo 方法');
            return;
        }

        // 获取所有内容容器元素
        const contentElements = currentSiteConfig.findContents(document);
        if (contentElements.length === 0) return;

        // 提取所有内容信息
        const contentInfos = contentElements
            .map(element => {
                const info = currentSiteConfig.extractContentInfo(element);
                return { element, ...info };
            })
            .filter(info => info.id); // 过滤掉无效的内容

        if (contentInfos.length === 0) return;

        // 只查询新出现的内容（使用缓存过滤）
        const newInfos = contentInfos.filter(info => !contentStatusMap.has(info.id));
        const contentIds = contentInfos.map(info => info.id);

        console.log(`[Bakabase] 页面总内容数: ${contentIds.length}, 已缓存: ${contentIds.length - newInfos.length}, 新内容: ${newInfos.length}`);

        // 先渲染下载按钮（无论是否有缓存）
        renderMarkers();

        if (newInfos.length === 0) {
            console.log('[Bakabase] 所有内容都已缓存，跳过查询');
            return;
        }

        // 仅查询新内容的状态
        const newIds = newInfos.map(info => info.id);
        queryContentStatus(newIds);
    }

    // 查询内容状态
    function queryContentStatus(contentIds) {
        const url = window.location.href;
        const filter = currentSiteConfig.extractFilter(url);

        console.log(`[Bakabase] 正在查询 ${contentIds.length} 个新内容的状态...`);

        GM_xmlhttpRequest({
            method: 'POST',
            url: `${API_BASE_URL}/third-party-content-tracker/query`,
            headers: {
                'Content-Type': 'application/json'
            },
            data: JSON.stringify({
                domainKey: currentSiteConfig.key,
                filter: filter,
                contentIds: contentIds
            }),
            onload: function(response) {
                if (response.status === 200) {
                    const result = JSON.parse(response.responseText);
                    if (result.data) {
                        // 更新状态映射（缓存查询结果）
                        let viewedCount = 0;
                        let updateCount = 0;
                        
                        result.data.forEach(item => {
                            contentStatusMap.set(item.contentId, {
                                isViewed: item.isViewed,
                                hasUpdate: item.hasUpdate,
                                viewedAt: item.viewedAt ? new Date(item.viewedAt) : null,
                                updatedAt: item.updatedAt ? new Date(item.updatedAt) : null
                            });
                            
                            if (item.isViewed) viewedCount++;
                            if (item.hasUpdate) updateCount++;
                        });

                        console.log(`[Bakabase] 查询完成: 总数 ${result.data.length}, 已看过 ${viewedCount}, 有更新 ${updateCount}, 缓存总数: ${contentStatusMap.size}`);

                        // 渲染标记
                        renderMarkers();
                    }
                }
            },
            onerror: function(error) {
                console.error('[Bakabase] 查询内容状态失败:', error);
            }
        });
    }

    // 渲染标记
    function renderMarkers() {
        if (!currentSiteConfig.renderMarker) return;
        if (!currentSiteConfig.findContents || !currentSiteConfig.extractContentInfo) {
            console.warn('[Bakabase] 网站配置未定义 findContents 或 extractContentInfo 方法');
            return;
        }

        // 获取所有内容容器元素
        const contentElements = currentSiteConfig.findContents(document);
        if (contentElements.length === 0) return;

        let renderedCount = 0;
        let viewedCount = 0;
        let updateCount = 0;

        contentElements.forEach(element => {
            // 提取内容信息
            const info = currentSiteConfig.extractContentInfo(element);
            if (!info.id) return;

            // 获取状态（如果有的话）
            const status = contentStatusMap.get(info.id) || {
                isViewed: false,
                hasUpdate: false,
                viewedAt: null,
                updatedAt: null
            };

            currentSiteConfig.renderMarker(element, status);
            renderedCount++;

            if (status.hasUpdate) {
                updateCount++;
            } else if (status.isViewed) {
                viewedCount++;
            }
        });

        console.log(`[Bakabase] 渲染完成: 共 ${renderedCount} 个标记（已看过 ${viewedCount}, 有更新 ${updateCount}）`);
    }

    // 检查元素是否在视口中可见
    function isElementInViewport(element) {
        const rect = element.getBoundingClientRect();
        return (
            rect.top >= 0 &&
            rect.left >= 0 &&
            rect.bottom <= (window.innerHeight || document.documentElement.clientHeight) &&
            rect.right <= (window.innerWidth || document.documentElement.clientWidth)
        );
    }

    // 标记视口中可见的、未标记过的内容
    function markVisibleContentAsViewed() {
        if (!currentSiteConfig) return;
        if (!currentSiteConfig.findContents || !currentSiteConfig.extractContentInfo) {
            console.warn('[Bakabase] 网站配置未定义 findContents 或 extractContentInfo 方法');
            return;
        }

        // 获取所有内容容器元素
        const contentElements = currentSiteConfig.findContents(document);
        if (contentElements.length === 0) return;

        // 找出在视口中可见且未标记为已看过的内容
        const visibleUnviewedItems = [];
        
        contentElements.forEach(element => {
            // 提取内容信息
            const info = currentSiteConfig.extractContentInfo(element);
            if (!info.id) return;

            // 检查是否已经标记为已看过
            const status = contentStatusMap.get(info.id);
            if (status && status.isViewed) {
                return; // 已经标记过，跳过
            }

            // 检查元素是否在视口中可见
            if (isElementInViewport(element)) {
                visibleUnviewedItems.push({
                    contentId: info.id,
                    element: element,
                    updatedAt: info.updateTime
                });
            }
        });

        if (visibleUnviewedItems.length === 0) {
            console.log('[Bakabase] 视口中没有未标记的内容');
            return;
        }

        console.log(`[Bakabase] 发现视口中有 ${visibleUnviewedItems.length} 个未标记的内容，准备标记...`, 
            visibleUnviewedItems.map(v => v.contentId));

        // 标记这些内容
        const url = window.location.href;
        const filter = currentSiteConfig.extractFilter(url);

        const contentItems = visibleUnviewedItems.map(item => ({
            contentId: item.contentId,
            updatedAt: item.updatedAt ? item.updatedAt.toISOString() : null
        }));

        const markApiUrl = `${API_BASE_URL}/third-party-content-tracker/mark-viewed`;
        console.log(`[Bakabase] Request to ${markApiUrl}`, visibleUnviewedItems)

        GM_xmlhttpRequest({
            method: 'POST',
            url: markApiUrl,
            headers: {
                'Content-Type': 'application/json'
            },
            data: JSON.stringify({
                domainKey: currentSiteConfig.key,
                filter: filter,
                contentItems: contentItems
            }),
            onload: function(response) {
                if (response.status === 200) {
                    console.log(`[Bakabase] 成功标记 ${visibleUnviewedItems.length} 个内容为已看过`);
                    // 更新本地状态
                    visibleUnviewedItems.forEach(item => {
                        const contentId = item.contentId;
                        const updatedAt = item.updatedAt;
                        
                        if (!contentStatusMap.has(contentId)) {
                            contentStatusMap.set(contentId, {
                                isViewed: true,
                                hasUpdate: false,
                                viewedAt: new Date(),
                                updatedAt: updatedAt
                            });
                        } else {
                            const status = contentStatusMap.get(contentId);
                            status.isViewed = true;
                            status.viewedAt = new Date();
                            status.hasUpdate = false;
                            // 保存当前的 updatedAt，用于后续比较
                            if (updatedAt) {
                                status.updatedAt = updatedAt;
                            }
                        }
                    });
                    // 重新渲染标记
                    renderMarkers();
                }
            },
            onerror: function(error) {
                console.error('[Bakabase] 标记已看过失败:', error);
            }
        });
    }

    // 添加设置面板（可选）
    function addSettingsPanel() {
        // 标记显示/隐藏状态
        let markersVisible = GM_getValue('markers_visible', true);

        // 创建设置面板容器
        const settingsPanel = document.createElement('div');
        settingsPanel.style.cssText = `
            position: fixed;
            bottom: 20px;
            right: 20px;
            display: flex;
            flex-direction: column;
            gap: 10px;
            z-index: 10000;
        `;

        // 创建 API 设置按钮
        const apiSettingsBtn = document.createElement('button');
        apiSettingsBtn.textContent = 'API 设置';
        apiSettingsBtn.style.cssText = `
            padding: 10px 15px;
            background-color: #2196F3;
            color: white;
            border: none;
            border-radius: 5px;
            cursor: pointer;
            font-size: 14px;
            white-space: nowrap;
        `;
        apiSettingsBtn.onclick = () => {
            const newUrl = prompt('请输入 Bakabase API 地址:', API_BASE_URL);
            console.log('[Bakabase] Setting API_BASE_URL to:', newUrl);
            if (newUrl) {
                GM_setValue('api_base_url', newUrl);
                alert(`设置已保存，请刷新页面生效`);
            }
        };

        // 创建显示/隐藏标记按钮
        const toggleMarkersBtn = document.createElement('button');
        toggleMarkersBtn.textContent = markersVisible ? '隐藏标记' : '显示标记';
        toggleMarkersBtn.style.cssText = `
            padding: 10px 15px;
            background-color: ${markersVisible ? '#ff9800' : '#4caf50'};
            color: white;
            border: none;
            border-radius: 5px;
            cursor: pointer;
            font-size: 14px;
            white-space: nowrap;
        `;

        // 切换标记显示/隐藏
        toggleMarkersBtn.onclick = () => {
            markersVisible = !markersVisible;
            GM_setValue('markers_visible', markersVisible);

            // 更新按钮样式和文本
            toggleMarkersBtn.textContent = markersVisible ? '隐藏标记' : '显示标记';
            toggleMarkersBtn.style.backgroundColor = markersVisible ? '#ff9800' : '#4caf50';

            // 切换所有标记的可见性
            const allMarkers = document.querySelectorAll('.bakabase-marker, .bakabase-viewed-overlay, .bakabase-download-btn');
            allMarkers.forEach(marker => {
                marker.style.display = markersVisible ? '' : 'none';
            });

            console.log(`[Bakabase] 标记${markersVisible ? '显示' : '隐藏'}`);
        };

        settingsPanel.appendChild(toggleMarkersBtn);
        settingsPanel.appendChild(apiSettingsBtn);
        document.body.appendChild(settingsPanel);

        // 根据保存的设置初始化标记可见性
        if (!markersVisible) {
            // 延迟执行以确保标记已经渲染
            setTimeout(() => {
                const allMarkers = document.querySelectorAll('.bakabase-marker, .bakabase-viewed-overlay, .bakabase-download-btn');
                allMarkers.forEach(marker => {
                    marker.style.display = 'none';
                });
            }, 100);
        }
    }

    // 启动脚本
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }

    // 添加设置面板
    addSettingsPanel();

})();
