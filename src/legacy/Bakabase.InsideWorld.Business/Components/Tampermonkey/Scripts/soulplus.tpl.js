// ==UserScript==
// @name         [Bakabase]SoulPlus
// @namespace    http://tampermonkey.net/
// @version      0.1
// @description  Auto select torrent to download.
// @author       You
// @resource     IMPORTED_CSS https://cdn.jsdelivr.net/npm/toastify-js/src/toastify.min.css
// @grant        GM_getResourceText
// @grant        GM_addStyle
// @match        https://www.north-plus.net/*
// @icon         https://www.google.com/s2/favicons?domain=exhentai.org
// @require      https://cdn.bootcdn.net/ajax/libs/jquery/3.6.0/jquery.min.js
// @require      https://cdn.jsdelivr.net/npm/toastify-js
// ==/UserScript==

(function () {
    'use strict';

    const my_css = GM_getResourceText("IMPORTED_CSS");
    GM_addStyle(my_css);

    $(document).on('click', '#thread_img .inner .section-text div>a, #thread_img .inner .section-title>a', function (e) {
        e.preventDefault();
        console.log($(this));
        const relativeUrl = $(this).attr('href');
        const url = new URL(relativeUrl, window.location.origin).href;
        $.ajax({
            url: `{appEndpoint}/download-task-parse-task`,
            type: 'POST',
            contentType: "application/json",
            data: JSON.stringify({
                1: [url]
            }),
            success: function (data) {
                Toastify({
                    text: "done",
                    duration: 3000,
                    style: {
                        width: '160px',
                        height: '32px',
                        'font-size': '24px',
                        background: "linear-gradient(to right, #00b09b, #96c93d)"
                    },
                }).showToast();
            },
            error: function (data) {
                alert('woops!'); //or whatever
            }
        });
    });
})();