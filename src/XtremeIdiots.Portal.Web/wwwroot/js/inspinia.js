/*
 *
 *   INSPINIA - Responsive Admin Theme
 *   version 2.9.3
 *
 */

// ReSharper disable UseOfImplicitGlobalInFunctionScope
// ReSharper disable UnusedLocals
// ReSharper disable StringLiteralTypo
// ReSharper disable CoercedEqualsUsing


$(document).ready(function () {

    // Fast fix for position issue with Popper.js (only if Popper is present)
    if (window.Popper &&
        Popper.Defaults &&
        Popper.Defaults.modifiers &&
        Popper.Defaults.modifiers.computeStyle) {
        Popper.Defaults.modifiers.computeStyle.gpuAcceleration = false;
    }

    // Add body-small class if window less than 768px
    if (window.innerWidth < 769) {
        $("body").addClass("body-small");
    } else {
        $("body").removeClass("body-small");
    }

    // Collapse ibox function
    $(".collapse-link").on("click",
        function (e) {
            e.preventDefault();
            var ibox = $(this).closest("div.ibox");
            var button = $(this).find("i");
            var content = ibox.children(".ibox-content");
            content.slideToggle(200);
            button.toggleClass("fa-chevron-up").toggleClass("fa-chevron-down");
            ibox.toggleClass("").toggleClass("border-bottom");
            setTimeout(function () {
                ibox.resize();
                ibox.find("[id^=map-]").resize();
            },
                50);
        });

    // Close ibox function
    $(".close-link").on("click",
        function (e) {
            e.preventDefault();
            var content = $(this).closest("div.ibox");
            content.remove();
        });

    // Fullscreen ibox function
    $(".fullscreen-link").on("click",
        function (e) {
            e.preventDefault();
            var ibox = $(this).closest("div.ibox");
            var button = $(this).find("i");
            $("body").toggleClass("fullscreen-ibox-mode");
            button.toggleClass("fa-expand").toggleClass("fa-compress");
            ibox.toggleClass("fullscreen");
            setTimeout(function () {
                $(window).trigger("resize");
            },
                100);
        });

    // Open close right sidebar (not used in current implementation)
    $(".right-sidebar-toggle").on("click",
        function (e) {
            e.preventDefault();
            $("#right-sidebar").toggleClass("sidebar-open");
        });

    // Open close small chat (not used in current implementation)
    $(".open-small-chat").on("click",
        function (e) {
            e.preventDefault();
            $(this).children().toggleClass("fa-comments").toggleClass("fa-times");
            $(".small-chat-box").toggleClass("active");
        });

    // Small todo handler
    $(".check-link").on("click",
        function () {
            var button = $(this).find("i");
            var label = $(this).next("span");
            button.toggleClass("fa-check-square").toggleClass("fa-square-o");
            label.toggleClass("todo-completed");
            return false;
        });

    // Bootstrap 5 Tooltips - Updated selector from data-toggle to data-bs-toggle
    $(".tooltip-demo").tooltip({
        selector: "[data-bs-toggle=tooltip]",
        container: "body"
    });

    // Move right sidebar top after scroll (not used in current implementation)
    $(window).scroll(function () {
        if ($(window).scrollTop() > 0 && !$("body").hasClass("fixed-nav")) {
            $("#right-sidebar").addClass("sidebar-top");
        } else {
            $("#right-sidebar").removeClass("sidebar-top");
        }
    });

    // Bootstrap 5 Popovers - Updated selector from data-toggle to data-bs-toggle
    $("[data-bs-toggle=popover]")
        .popover();
});

// Minimalize menu when screen is less than 768px
$(window).bind("resize",
    function () {
        if (window.innerWidth < 769) {
            $("body").addClass("body-small");
        } else {
            $("body").removeClass("body-small");
        }
    });

// check if browser support HTML5 local storage
function localStorageSupport() {
    return (("localStorage" in window) && window["localStorage"] !== null);
}

// For demo purpose - animation css script
function animationHover(element, animation) {
    element = $(element);
    element.hover(
        function () {
            element.addClass("animated " + animation);
        },
        function () {
            //wait for animation to finish before removing classes
            window.setTimeout(function () {
                element.removeClass("animated " + animation);
            },
                2000);
        });
}

// Dragable panels
function WinMove() {
    var element = "[class*=col]";
    var handle = ".ibox-title";
    var connect = "[class*=col]";
    $(element).sortable(
        {
            handle: handle,
            connectWith: connect,
            tolerance: "pointer",
            forcePlaceholderSize: true,
            opacity: 0.8
        })
        .disableSelection();
}