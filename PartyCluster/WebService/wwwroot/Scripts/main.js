function Api() {
    this.serviceUrl = location.protocol + '//' + location.hostname + (location.port ? ':' + location.port : '') + '/partyclusters';

    this.GetClusters = function (result) {
        this.httpGetJson(this.serviceUrl + '/api/clusters', result);
    };


    this.httpGetJson = function (url, result) {
        $.ajax({
            url: url,
            type: 'GET',
            contentType: 'application/json',
            datatype: 'json',
            cache: false
        })
	   .done(function (data) {
	       result(data);
	   })
	   .fail(function () {
	       $.ajax(this);
	       return;
	   });
    }
};

function PartyClusters(api) {
    var self = this;
    this.api = api;

    this.Initialize = function () {
        this.PopulateClusterList();

        setInterval(this.PopulateClusterList, 5000);
    };

    this.PopulateClusterList = function () {
        self.api.GetClusters(function (data) {
            var clusterTable = $('.cluster-list table');
            clusterTable.empty();

            $('<thead/>')
               .append(
                   $('<tr/>')
                       .append(
                           $('<th/>').text('Name'))
                       .append(
                           $('<th/>').text('Users'))
                       .append(
                           $('<th />').text('Applications'))
                       .append(
                           $('<th />').text('Services'))
                       .append(
                           $('<th />').text('Time left'))
                       .append(
                           $('<th />').text(''))
                       )
               .appendTo(clusterTable);

            $('<tbody>')
				.appendTo(clusterTable);

            $.each(data, function (id, jObject) {
                $('<tr/>')
					.append(
						$('<td/>').text(jObject.Name))
					.append(
						$('<td/>').text(jObject.UserCount))
					.append(
						$('<td/>').text(jObject.AppCount))
					.append(
						$('<td/>').text(jObject.ServiceCount))
					.append(
						$('<td/>').text(jObject.TimeRemaining))
					.append(
						$('<td/>')
							.append(
								$('<a class="button">').text('Join!')))
				.appendTo(clusterTable);

            });
        });
    };
};
