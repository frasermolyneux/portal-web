$(function () {
    const tableElement = $('#teamAccessTable');
    const gameType = tableElement.data('game-type');
    const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;

    tableElement.DataTable({
        processing: true,
        serverSide: true,
        searchDelay: 800,
        responsive: { details: { type: 'inline', target: 'tr' } },
        ajax: {
            url: '/User/GetGameModeratorsAjax?gameType=' + encodeURIComponent(gameType),
            contentType: 'application/json',
            type: 'POST',
            data: data => JSON.stringify(data),
            beforeSend: xhr => {
                if (token) xhr.setRequestHeader('RequestVerificationToken', token);
            }
        },
        columns: [
            {
                data: 'displayName', name: 'displayName',
                render: (data, type, row) => '<a href="/User/ManageProfile/' + encodeURIComponent(row.userProfileId) + '">' + $('<div>').text(data || '—').html() + '</a>'
            },
            {
                data: 'claims', orderable: false,
                render: claims => claims.some(claim => claim.systemGenerated && claim.claimType === 'Moderator')
                    ? '<span class="badge bg-info">Inherited Moderator role</span>' : 'Moderator'
            },
            {
                data: 'claims', orderable: false,
                render: claims => claims.filter(claim => !claim.systemGenerated)
                    .map(claim => $('<div>').text(claim.claimType + ' (' + claim.claimValue + ')').html()).join('<br>') || 'None'
            },
            {
                data: 'userProfileId', orderable: false,
                render: id => '<a class="btn btn-outline-secondary btn-sm" href="/User/ManageProfile/' + encodeURIComponent(id) + '" aria-label="Manage profile">Manage Profile</a>'
            }
        ]
    });
});
