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

    // Open close right sidebar
    $(".right-sidebar-toggle").on("click",
        function (e) {
            e.preventDefault();
            $("#right-sidebar").toggleClass("sidebar-open");
        });

    // Initialize slimscroll for right sidebar
    $(".sidebar-container").slimScroll({
        height: "100%",
        railOpacity: 0.4,
        wheelStep: 10
    });

    // Open close small chat
    $(".open-small-chat").on("click",
        function (e) {
            e.preventDefault();
            $(this).children().toggleClass("fa-comments").toggleClass("fa-times");
            $(".small-chat-box").toggleClass("active");
        });

    // Initialize slimscroll for small chat
    $(".small-chat-box .content").slimScroll({
        height: "234px",
        railOpacity: 0.4
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

    // Tooltips demo
    $(".tooltip-demo").tooltip({
        selector: "[data-toggle=tooltip]",
        container: "body"
    });

    // Move right sidebar top after scroll
    $(window).scroll(function () {
        if ($(window).scrollTop() > 0 && !$("body").hasClass("fixed-nav")) {
            $("#right-sidebar").addClass("sidebar-top");
        } else {
            $("#right-sidebar").removeClass("sidebar-top");
        }
    });

    $("[data-toggle=popover]")
        .popover();

    // Add slimscroll to element
    $(".full-height-scroll").slimscroll({
        height: "100%"
    });
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