function Api() {
    var self = this;
    this.serviceUrl = location.protocol + '//' + location.hostname + (location.port ? ':' + location.port : '') + '/partyclusters';

    this.GetClusters = function (result) {
        this.httpGetJson(self.serviceUrl + '/api/clusters', result);
    };

    this.JoinCluster = function (clusterId, username, useremail, result, failure) {
        $.ajax({
            url: self.serviceUrl + '/api/clusters/join/' + clusterId,
            type: 'POST',
            contentType: 'application/json',
            datatype: 'json',
            data: "{ UserName: '" + username + "', UserEmail: '" + useremail + "' }"
        })
	   .done(function (data) {
	       result(data);
	   })
	   .fail(function () {
	       failure();
	       return;
	   });
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

function Dialog($modal) {
    var overlay = $('.overlay');

    this.Window = $modal;

    this.Show = function () {
        overlay.fadeIn(300);
        $modal.fadeIn(300);
    };

    overlay.click(function () {
        $(this).fadeOut(200);
        $modal.fadeOut(200);
    });
};

function PartyClusters(api) {
    var self = this;
    this.api = api;
    this.selectedClusterId = 0;
    this.joinClusterDialog = new Dialog($('.join-cluster'));

    this.Initialize = function () {
        this.PopulateClusterList();

        $('.join-now', self.joinClusterDialog.Window).click(function () {
            var name = $('#join-username', self.joinClusterDialog.Window).val();
            var email = $('#join-useremail', self.joinClusterDialog.Window).val();

            self.api.JoinCluster(self.selectedClusterId, name, email)
        });

        $('.partynow').click(function () {
            alert('join random');
        });

        setInterval(this.PopulateClusterList, 5000);
    };

    this.ShowJoinClusterDialog = function (clusterId, clusterName) {
        self.selectedClusterId = clusterId;
        $('.join-cluster-name', self.joinClusterDialog.Window).text(clusterName);
        self.joinClusterDialog.Show();
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
                           $('<th/>').text('Applications'))
                       .append(
                           $('<th/>').text('Services'))
                       .append(
                           $('<th/>').text('Time left'))
                       .append(
                           $('<th/>').text(''))
                       )
               .appendTo(clusterTable);

            $('<tbody>')
				.appendTo(clusterTable);

            $.each(data, function (id, jObject) {
                $('<tr />')
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
								$('<a href="javascript:void(0);" class="button">')
									.text('Join!')
									.click(function () { self.ShowJoinClusterDialog(jObject.ClusterId, jObject.Name); })))
				.appendTo(clusterTable);

            });
        });
    };
};
