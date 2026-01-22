// XtremeIdiots Portal - Mobile Navigation Enhancement
// Fixes: jumping, two-click requirement, click-through, positioning issues

(function() {
    'use strict';

    // Mobile navigation controller
    const MobileNav = {
        backdrop: null,
        isInitialized: false,

        init: function() {
            if (this.isInitialized) return;
            this.isInitialized = true;

            // Create backdrop element for mobile menu overlay
            this.createBackdrop();

            // Setup menu toggle handler
            this.setupMenuToggle();

            // Setup Bootstrap collapse event handlers
            this.setupCollapseHandlers();

            // Fix SmoothlyMenu conflicts on mobile
            this.fixSmoothlyMenuForMobile();

            // Setup window resize handler
            this.setupResizeHandler();

            // Initialize menu state
            this.initializeMenuState();
        },

        isMobile: function() {
            return window.innerWidth <= 768;
        },

        createBackdrop: function() {
            if (document.querySelector('.mobile-menu-backdrop')) return;

            this.backdrop = document.createElement('div');
            this.backdrop.className = 'mobile-menu-backdrop';
            this.backdrop.setAttribute('aria-hidden', 'true');
            
            // Close menu when clicking backdrop
            this.backdrop.addEventListener('click', () => {
                this.closeMenu();
            });

            document.body.appendChild(this.backdrop);
        },

        setupMenuToggle: function() {
            const toggleBtn = document.querySelector('.navbar-minimalize');
            if (!toggleBtn) return;

            toggleBtn.addEventListener('click', (e) => {
                e.preventDefault();
                e.stopPropagation();

                if (this.isMobile()) {
                    this.toggleMobileMenu();
                } else {
                    // Desktop behavior - let Inspinia handle it
                    document.body.classList.toggle('mini-navbar');
                }
            });
        },

        toggleMobileMenu: function() {
            const body = document.body;
            const isOpen = !body.classList.contains('mini-navbar');

            if (isOpen) {
                this.closeMenu();
            } else {
                this.openMenu();
            }
        },

        openMenu: function() {
            const body = document.body;
            body.classList.remove('mini-navbar');
            
            // Show backdrop
            if (this.backdrop) {
                this.backdrop.classList.add('show');
            }

            // Update ARIA
            const toggleBtn = document.querySelector('.navbar-minimalize');
            if (toggleBtn) {
                toggleBtn.setAttribute('aria-expanded', 'true');
            }

            // Prevent body scroll on mobile when menu is open
            if (this.isMobile()) {
                body.style.overflow = 'hidden';
            }
        },

        closeMenu: function() {
            const body = document.body;
            body.classList.add('mini-navbar');

            // Hide backdrop
            if (this.backdrop) {
                this.backdrop.classList.remove('show');
            }

            // Update ARIA
            const toggleBtn = document.querySelector('.navbar-minimalize');
            if (toggleBtn) {
                toggleBtn.setAttribute('aria-expanded', 'false');
            }

            // Restore body scroll
            body.style.overflow = '';
        },

        setupCollapseHandlers: function() {
            const sideMenu = document.getElementById('side-menu');
            if (!sideMenu) return;

            // Get all collapsible submenu triggers
            const collapseTriggers = sideMenu.querySelectorAll('[data-bs-toggle="collapse"]');

            collapseTriggers.forEach(trigger => {
                // Prevent default anchor behavior
                trigger.addEventListener('click', (e) => {
                    if (this.isMobile()) {
                        e.preventDefault();
                        e.stopPropagation();

                        const targetId = trigger.getAttribute('href');
                        const targetElement = document.querySelector(targetId);

                        if (targetElement) {
                            this.toggleSubmenu(trigger, targetElement);
                        }
                    }
                });

                // Handle keyboard navigation
                trigger.addEventListener('keydown', (e) => {
                    if (e.key === 'Enter' || e.key === ' ') {
                        e.preventDefault();
                        e.stopPropagation();
                        trigger.click();
                    }
                });
            });

            // Mark parent as active when submenu is shown
            sideMenu.querySelectorAll('.collapse').forEach(collapse => {
                collapse.addEventListener('show.bs.collapse', function() {
                    const parentLi = this.closest('li');
                    if (parentLi) {
                        parentLi.classList.add('active');
                    }
                });

                collapse.addEventListener('hide.bs.collapse', function() {
                    const parentLi = this.closest('li');
                    if (parentLi && !this.classList.contains('show')) {
                        parentLi.classList.remove('active');
                    }
                });
            });
        },

        toggleSubmenu: function(trigger, submenu) {
            const isExpanded = submenu.classList.contains('show');
            const parentLi = trigger.closest('li');

            if (isExpanded) {
                // Close submenu
                submenu.classList.remove('show');
                trigger.setAttribute('aria-expanded', 'false');
                submenu.setAttribute('aria-expanded', 'false');
                if (parentLi) parentLi.classList.remove('active');
            } else {
                // Close other submenus first (accordion behavior)
                if (this.isMobile()) {
                    this.closeAllSubmenus(submenu);
                }

                // Open submenu
                submenu.classList.add('show');
                trigger.setAttribute('aria-expanded', 'true');
                submenu.setAttribute('aria-expanded', 'true');
                if (parentLi) parentLi.classList.add('active');
            }
        },

        closeAllSubmenus: function(except) {
            const allSubmenus = document.querySelectorAll('#side-menu .collapse');
            allSubmenus.forEach(submenu => {
                if (submenu !== except && submenu.classList.contains('show')) {
                    const trigger = document.querySelector(`[href="#${submenu.id}"]`);
                    if (trigger) {
                        submenu.classList.remove('show');
                        trigger.setAttribute('aria-expanded', 'false');
                        const parentLi = trigger.closest('li');
                        if (parentLi) parentLi.classList.remove('active');
                    }
                }
            });
        },

        fixSmoothlyMenuForMobile: function() {
            // Override or patch the SmoothlyMenu function to prevent conflicts on mobile
            if (typeof window.SmoothlyMenu === 'function') {
                const originalSmoothlyMenu = window.SmoothlyMenu;
                window.SmoothlyMenu = function() {
                    // Skip animation on mobile to prevent menu state loss
                    if (window.innerWidth < 769) {
                        return;
                    }
                    originalSmoothlyMenu();
                };
            }
        },

        setupResizeHandler: function() {
            let resizeTimer;
            window.addEventListener('resize', () => {
                clearTimeout(resizeTimer);
                resizeTimer = setTimeout(() => {
                    this.handleResize();
                }, 250);
            });
        },

        handleResize: function() {
            if (!this.isMobile()) {
                // Switching to desktop - close mobile menu and clean up
                if (this.backdrop) {
                    this.backdrop.classList.remove('show');
                }
                document.body.style.overflow = '';
            } else {
                // Switching to mobile - ensure menu is closed by default
                if (!document.body.classList.contains('mini-navbar')) {
                    this.closeMenu();
                }
            }
        },

        initializeMenuState: function() {
            // On mobile, start with menu closed
            if (this.isMobile()) {
                document.body.classList.add('mini-navbar');
                if (this.backdrop) {
                    this.backdrop.classList.remove('show');
                }

                // Ensure currently active submenus stay open
                const activeSubmenus = document.querySelectorAll('#side-menu .collapse.show');
                activeSubmenus.forEach(submenu => {
                    const parentLi = submenu.closest('li');
                    if (parentLi) {
                        parentLi.classList.add('active');
                    }
                    
                    const trigger = document.querySelector(`[href="#${submenu.id}"]`);
                    if (trigger) {
                        trigger.setAttribute('aria-expanded', 'true');
                    }
                });
            }
        }
    };

    // Initialize when DOM is ready
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', () => {
            MobileNav.init();
        });
    } else {
        MobileNav.init();
    }

    // Make MobileNav available globally for debugging
    window.MobileNav = MobileNav;

})();
