function escapeHtml(unsafe) {
    return unsafe
        .replace(/&/g, "&amp;")
        .replace(/</g, "&lt;")
        .replace(/>/g, "&gt;")
        .replace(/"/g, "&quot;")
        .replace(/'/g, "&#039;");
}

function handleAjaxError(xhr, textStatus, error) {
    console.log(textStatus);
}

function renderPlayerName(gameType, username, playerId) {
    var coloredName = CodColors.renderSafe(username);
    var icon = typeof gameType === 'number' ? gameTypeIconEnum(gameType) : gameTypeIcon(gameType);
    if (playerId) {
        return icon + " <a href='/Players/Details/" + playerId + "'>" + coloredName + "</a>";
    }
    return icon + " " + coloredName;
}

function chatLogUrl(chatMessageId) {
    return "<a href='/ServerAdmin/ChatLogPermaLink/" + chatMessageId + "'>PermLink</a>";
}

function gameTypeIcon(gameType) {
    return "<img src='/images/game-icons/" +
        gameType +
        ".png' alt='" +
        gameType +
        "' width='16' height='16' />";
}

function gameTypeIconEnum(gameType) {
    var map = {
        0: "Unknown",
        1: "CallOfDuty2",
        2: "CallOfDuty4",
        3: "CallOfDuty5",
        4: "Insurgency",
        5: "ArkSurvivalEvolved",
        6: "Battlefield1",
        7: "Battlefield3",
        8: "Battlefield4",
        9: "Battlefield5",
        10: "BattlefieldBadCompany2",
        11: "CrysisWars",
        12: "Left4Dead2",
        13: "Minecraft",
        14: "PlayerUnknownsBattleground",
        15: "RisingStormVietnam",
        16: "Rust",
        17: "WarThunder",
        18: "WorldOfWarships",
        19: "WorldWar3",
        20: "UnrealTournament2004",
        21: "Arma",
        22: "Arma2",
        23: "Arma3"
    };
    var gameTypeString = map[gameType] || "Unknown";

    return "<img src='/images/game-icons/" +
        gameTypeString +
        ".png' alt='" +
        gameTypeString +
        "' width='16' height='16' />";
}

// Returns a font-awesome icon followed by the admin action type text.
// Mirrors mappings used in server-side views (AdminActions ViewComponent).
function adminActionTypeIcon(actionType) {
    if (!actionType) return '';
    var iconClass = 'fa-solid fa-circle-question';
    switch (actionType) {
        case 'Observation':
            iconClass = 'fa-solid fa-eye';
            break;
        case 'Warning':
            iconClass = 'fa-solid fa-triangle-exclamation';
            break;
        case 'Kick':
            iconClass = 'fa-solid fa-user-xmark';
            break;
        case 'TempBan':
            iconClass = 'fa-solid fa-clock';
            break;
        case 'Ban':
            iconClass = 'fa-solid fa-ban';
            break;
    }
    return "<i class='" + iconClass + "' aria-hidden='true'></i> <span class='action-text'>" + actionType + "</span>";
}

// Global DataTables defaults for consistent empty state messaging
if (typeof $.fn !== 'undefined' && typeof $.fn.dataTable !== 'undefined') {
    $.extend(true, $.fn.dataTable.defaults, {
        language: {
            emptyTable: 'No records found',
            zeroRecords: 'No matching records found'
        }
    });
}

// Global DataTables footer spacing helper: ensures wrapper gets padding to avoid footer overlap.
(function () {
    function applyDataTableSpacing() {
        var wrapper = document.querySelector('.wrapper.wrapper-content');
        if (!wrapper) return;
        if (document.querySelector('.dataTables_wrapper')) {
            wrapper.classList.add('with-datatable');
        }
    }

    // Run after DOM ready with slight delay (allow DataTables init).
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', function () { setTimeout(applyDataTableSpacing, 400); });
    } else {
        setTimeout(applyDataTableSpacing, 400);
    }

    // Observe for late-injected tables (AJAX navigation or deferred init).
    var observer = new MutationObserver(function (mutations) {
        for (var i = 0; i < mutations.length; i++) {
            if (mutations[i].addedNodes && mutations[i].addedNodes.length) {
                if (document.querySelector('.dataTables_wrapper')) {
                    applyDataTableSpacing();
                    observer.disconnect();
                    break;
                }
            }
        }
    });
    try {
        observer.observe(document.body, { childList: true, subtree: true });
    } catch (e) {
        // Silently ignore if observe fails (very old browsers)
    }
})();

// Global toastr configuration (only if library loaded)
(function () {
    if (typeof toastr === 'undefined') return;
    toastr.options = Object.assign({
        closeButton: true,
        progressBar: true,
        newestOnTop: true,
        preventDuplicates: true,
        timeOut: 3000,
        extendedTimeOut: 1500,
        positionClass: 'toast-top-right'
    }, toastr.options || {});

    // Unified helper for showing toast messages; type can be success|info|warning|error
    window.showToast = function (type, message, title, opts) {
        if (!message) return;
        var fn = (toastr[type] || toastr.info).bind(toastr);
        fn(message, title || '', opts || {});
    };
})();

// Process server-sent alerts (TempData) and convert to toastr notifications.
(function () {
    var container = document.getElementById('server-alerts-data');
    if (!container) return;
    var json = container.getAttribute('data-alerts');
    if (!json) return;
    var alerts;
    try { alerts = JSON.parse(json); } catch { return; }
    if (!alerts || !alerts.length) return;

    var mapBootstrapToToastr = function (bootstrapType) {
        if (!bootstrapType) return 'info';
        if (bootstrapType.indexOf('success') !== -1) return 'success';
        if (bootstrapType.indexOf('danger') !== -1) return 'error';
        if (bootstrapType.indexOf('warning') !== -1) return 'warning';
        return 'info';
    };

    alerts.forEach(function (a) {
        var toastType = mapBootstrapToToastr(a.type || a.Type);
        var msg = a.message || a.Message;
        if (typeof toastr !== 'undefined') {
            showToast(toastType, msg);
        } else {
            // Fallback: inject a bootstrap alert dynamically (JS enabled but toastr not loaded)
            var fallback = document.createElement('div');
            fallback.className = 'alert ' + (a.type || a.Type) + ' mt-2';
            fallback.setAttribute('role', 'alert');
            fallback.textContent = msg;
            container.appendChild(fallback);
        }
    });
})();

function downloadDemoLink(demoName, demoId) {
    return "<a href='/Demos/Download/" + demoId + "'>" + demoName + "</a>";
}

function deleteDemoLink(demoId, gameType = null) {
    if (gameType === null) {
        return '<div class="btn-group btn-group-sm" role="group">' +
            '<a type="button" class="btn btn-outline-danger"  href="/Demos/Delete/' +
            demoId +
            '"><i class="fa-solid fa-fw fa-trash"></i> Delete Demo</a>' +
            "</div>";
    } else {
        return '<div class="btn-group btn-group-sm" role="group">' +
            '<a type="button" class="btn btn-outline-danger"  href="/Demos/Delete/' +
            demoId +
            '?filterGame=true"><i class="fa-solid fa-fw fa-trash"></i> Delete Demo</a>' +
            "</div>";
    }
}

function geoLocationIpLink(ipAddress) {
    return "<a href='https://www.geo-location.net/Home/LookupAddress/" + ipAddress + "'>" + ipAddress + "</a>";
}

/**
 * Formats an IP address with consistent display including:
 * {country flag} {IP Address} {Risk Pill} {Type Pill} {VPN/Proxy Pills}
 * 
 * @param {string} ipAddress - The IP address to format
 * @param {number} riskScore - The risk score (0-100) from ProxyCheck
 * @param {boolean} isProxy - Whether the IP is a proxy
 * @param {boolean} isVpn - Whether the IP is a VPN
 * @param {string} type - The type of proxy/VPN
 * @param {string} countryCode - Optional country code for the flag
 * @param {boolean} linkToDetails - Whether to link the IP to the details page (default: true)
 * @returns {string} HTML formatted IP address
 */
function formatIPAddress(ipAddress, riskScore, isProxy, isVpn, type = '', countryCode = '', linkToDetails = true) {
    if (!ipAddress) return '';
    let result = '';

    // 1. Country Flag (skip if unknown to avoid broken image)
    if (countryCode && countryCode !== '') {
        result += "<img src='/images/flags/" + countryCode.toLowerCase() + ".png' alt='" + countryCode + " flag' /> ";
    }

    // 2. IP Address (with or without link)
    if (linkToDetails) {
        result += "<a href='/IPAddresses/Details?ipAddress=" + ipAddress + "'>" + ipAddress + "</a> ";
    } else {
        result += ipAddress + " ";
    }

    // 3. Risk Score Pill
    if (riskScore !== null && riskScore !== undefined && riskScore > 0) {
        let riskClass = 'text-bg-success';
        if (riskScore >= 80) {
            riskClass = 'text-bg-danger';
        } else if (riskScore >= 50) {
            riskClass = 'text-bg-warning';
        } else if (riskScore >= 25) {
            riskClass = 'text-bg-info';
        }

        result += "<span class='badge rounded-pill " + riskClass + "'>Risk: " + riskScore + "</span> ";
    }

    // 4. Type Pill
    if (type && type !== '') {
        result += "<span class='badge rounded-pill text-bg-primary'>" + type + "</span> ";
    }

    // 5. Proxy Pill
    if (isProxy) {
        result += "<span class='badge rounded-pill text-bg-danger'>Proxy</span> ";
    }

    // 6. VPN Pill
    if (isVpn) {
        result += "<span class='badge rounded-pill text-bg-warning'>VPN</span>";
    }

    return result;
}

function manageClaimsLink(userId) {
    return '<div class="btn-group btn-group-sm" role="group">' +
        '<a type="button" class="btn btn-outline-secondary"  href="/User/ManageProfile/' +
        userId +
        '?filterGame=true"><i class="fa-solid fa-fw fa-key"></i> Manage Claims</a>' +
        "</div>";
}

function logOutUserLink(id, antiForgeryToken) {
    return '<form class="form-inline" method="post" action="/User/LogUserOut" method="post">' +
        '<input id="id" name="id" type="hidden" value="' +
        id +
        '\"/>' +
        '<button class="btn btn-outline-secondary" type="submit"><i class="fa-solid fa-fw fa-arrow-right-from-bracket"></i> Logout User</button>' +
        antiForgeryToken +
        '</form>';
}
