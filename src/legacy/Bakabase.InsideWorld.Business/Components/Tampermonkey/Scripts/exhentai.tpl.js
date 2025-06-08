// ==UserScript==
// @name         [Bakabase]ExHentai
// @namespace    http://tampermonkey.net/
// @version      0.1
// @description  Auto select torrent to download.
// @author       You
// @resource     IMPORTED_CSS https://cdn.jsdelivr.net/npm/toastify-js/src/toastify.min.css
// @grant        GM_getResourceText
// @grant        GM_addStyle
// @match        https://exhentai.org/*
// @icon         https://www.google.com/s2/favicons?domain=exhentai.org
// @require      https://cdn.bootcdn.net/ajax/libs/jquery/3.6.0/jquery.min.js
// @require      https://cdn.jsdelivr.net/npm/toastify-js
// ==/UserScript==

(function () {
    'use strict';

    const my_css = GM_getResourceText("IMPORTED_CSS");
    GM_addStyle(my_css);

    const getTorrentPriority = e => {
        const downloadable = $(e).find('tr').eq(2).find('a').length > 0;
        if (!downloadable) {
            return Number.MAX_VALUE;
        }
        return -parseInt(/\d+/.exec($(e).find('tr td').eq(5).text())[0]);
    };

    window.downloadTorrent = (downloadUrl, galleryUrl) => {
        // https://exhentai.org/gallerytorrents.php?gid=1968499&t=8806ea0d88
        $.get(downloadUrl, c => {
            const $cq = $($.parseHTML(c));
            const $tables = $cq.find('#torrentinfo table');
            const $candidate = $tables.sort((a, b) => {
                return getTorrentPriority(a) - getTorrentPriority(b);
            }).eq(0);
            // console.log($candidate.text());
            location.href = $candidate.find('tr').eq(2).find('a').attr('href');
        });
    }

    $(document).on('click', '.gl1t .gl3t>a', function (e) {
        e.preventDefault();
        console.log($(this));
        const url = $(this).attr('href');
        $.ajax({
            url: `{appEndpoint}/download-task/exhentai`,
            type: 'POST',
            contentType: "application/json",
            data: JSON.stringify({
                type: 4,
                link: url
            }),
            success: function (data) {
                if (data.code) {
                    alert(data.message);
                    return;
                }
                Toastify({
                    text: "Added",
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

    console.log(window.downloadTorrent)

    $('.gl1t .gl5t .gldown').each((i, e) => {
        const $e = $(e);
        const $a = $e.find('a');
        if ($a.length > 0) {
            $e.css('display', 'flex');
            const downloadUrl = $a.attr('href');
            const galleryUrl = $a.parents('.gl1t').children('a').eq(0).attr('href');
            // console.log(downloadUrl, galleryUrl);
            const $elem = $(`<a><img src="https://exhentai.org/img/t.png" alt="T" title="Show torrents"></a>`);
            $elem.click(() => {
                console.log(downloadUrl, galleryUrl);
                window.downloadTorrent(downloadUrl, galleryUrl); return false;
            });
            $e.append($elem);
        }
    });
})();