function Api() {
    var self = this;
    this.refreshRate = 5000;
    this.serviceUrl = location.protocol + '//' + location.hostname + (location.port ? ':' + location.port : '') + '/partyclusters';

    this.GetClusters = function (result) {
        this.httpGetJson(self.serviceUrl + '/api/clusters', result);
    };

    this.JoinCluster = function (clusterId, useremail, result, failure) {
        $.ajax({
            url: self.serviceUrl + '/api/clusters/join/' + clusterId,
            type: 'POST',
            contentType: 'application/json',
            datatype: 'json',
            data: "{ UserEmail: '" + useremail + "' }"
        })
	   .done(function (data) {
	       result(data);
	   })
	   .fail(function (xhr) {
	       var json = $.parseJSON(xhr.responseText);
	       failure(json);
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
    this.refreshRate = 5000;
    this.selectedClusterId = 0;
    this.autoRetry = false;
    this.joinClusterDialog = new Dialog($('.join-cluster'));

    this.Initialize = function () {
        this.PopulateClusterList();

        $('.join-now', self.joinClusterDialog.Window).click(function () {
            self.JoinCluster();
        });

        $('.partynow').click(function () {
            try {
                var id = self.SelectRandomCluster();
                self.ShowJoinClusterDialog(id, 'Join now!', true);
            }
            catch (exception) {
                alert('no clusters available. Come back later');
            }
        });

        setInterval(this.PopulateClusterList, self.refreshRate);
    };

    this.JoinCluster = function () {
        var email = $('#join-useremail', self.joinClusterDialog.Window).val();

        self.api.JoinCluster(self.selectedClusterId, email,
			function (data) {

			},
			function (data) {
			    if (self.autoRetry) {
			        switch (data.Code) {
			            case "ClusterFull":
			            case "ClusterExpired":
			            case "ClusterNotReady":
			            case "ClusterDoesNotExist":
			            case "NoPortsAvailable":
			                self.selectedClusterId = self.SelectRandomCluster();
			                setTimeout(self.JoinCluster, self.refreshRate)
			                break;
			        }
			    }
			}
        );
    }



    this.SelectRandomCluster = function () {
        var tableRows = $('.cluster-list table tr');
        var clusterId = 0;
        var clusterName = '';
        for (var i = 0; i < tableRows.length; ++i) {
            var $row = $(tableRows[i]);
            if ($row.attr('data-users') < $row.attr('data-capacity')) {
                return $row.attr('data-id');
            }
        }
        throw "No clusters currently available";
    };

    this.ShowJoinClusterDialog = function (clusterId, clusterName, autoretry) {
        self.selectedClusterId = clusterId;
        self.autoRetry = autoretry;
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
                $('<tr data-id="' + jObject.ClusterId + '" data-users="' + jObject.UserCount + '" data-capacity="' + jObject.Capacity + '" />')
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
                                    .click(function () { self.ShowJoinClusterDialog(jObject.ClusterId, jObject.Name, false); })))
            .appendTo(clusterTable);

            });
        });
    };
};
