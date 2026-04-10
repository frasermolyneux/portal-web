// Game Servers Reorder - Drag and drop reordering with save
$(document).ready(function () {
    var $list = $('#gameServerReorderList');
    if ($list.length === 0) return;

    var $saveBtn = $('#saveOrderBtn');
    var $feedback = $('#reorderFeedback');
    var token = $('input[name="__RequestVerificationToken"]').val();

    function updatePositionBadges() {
        $list.find('.game-server-item').each(function (i) {
            $(this).attr('data-index', i);
            $(this).find('.game-server-position-badge').text(i + 1);
        });
    }

    // Attach HTML5 drag-and-drop to each item
    $list.on('dragstart', '.game-server-item', function (e) {
        e.originalEvent.dataTransfer.setData('text/plain', $(this).index().toString());
        $(this).addClass('opacity-50 game-server-item--dragging');
    });

    $list.on('dragend', '.game-server-item', function () {
        $(this).removeClass('opacity-50 game-server-item--dragging');
    });

    $list.on('dragover', '.game-server-item', function (e) {
        e.preventDefault();
        $(this).addClass('border-primary');
    });

    $list.on('dragleave', '.game-server-item', function () {
        $(this).removeClass('border-primary');
    });

    $list.on('drop', '.game-server-item', function (e) {
        e.preventDefault();
        $(this).removeClass('border-primary');

        var fromIndex = parseInt(e.originalEvent.dataTransfer.getData('text/plain'));
        var toIndex = $(this).index();

        if (fromIndex !== toIndex) {
            var $items = $list.children('.game-server-item');
            var $moved = $items.eq(fromIndex).detach();

            if (toIndex === 0) {
                $list.prepend($moved);
            } else if (fromIndex < toIndex) {
                $list.children('.game-server-item').eq(toIndex - 1).after($moved);
            } else {
                $list.children('.game-server-item').eq(toIndex).before($moved);
            }

            updatePositionBadges();
        }
    });

    function showFeedback(message, isError) {
        $feedback.removeClass('alert-success alert-danger')
            .addClass(isError ? 'alert alert-danger' : 'alert alert-success')
            .text(message)
            .show();
    }

    $saveBtn.on('click', function () {
        var gameServerIds = [];
        $list.find('.game-server-item').each(function () {
            gameServerIds.push($(this).attr('data-server-id'));
        });

        $saveBtn.prop('disabled', true).html('<i class="fa-solid fa-spinner fa-spin"></i> Saving...');
        $feedback.hide();

        $.ajax({
            url: '/GameServers/UpdateOrder',
            type: 'POST',
            contentType: 'application/json',
            headers: { 'RequestVerificationToken': token },
            data: JSON.stringify(gameServerIds),
            success: function (response) {
                if (response && response.success) {
                    showFeedback(response.message || 'Server order saved successfully.', false);
                } else {
                    showFeedback((response && response.message) || 'Failed to save server order.', true);
                }
            },
            error: function (xhr) {
                var msg = 'An error occurred while saving.';
                try {
                    var resp = JSON.parse(xhr.responseText);
                    if (resp && resp.message) msg = resp.message;
                } catch (e) { /* ignore */ }
                showFeedback(msg, true);
            },
            complete: function () {
                $saveBtn.prop('disabled', false).html('<i class="fa-solid fa-floppy-disk"></i> Save Order');
            }
        });
    });
});
