function Api() {
    var self = this;
    this.appPath = '';
    this.refreshRate = 5000;
    this.serviceUrl = location.protocol + '//' + location.hostname + (location.port ? ':' + location.port : '') + self.appPath;

    this.GetClusters = function (result) {
        this.httpGetJson(self.serviceUrl + '/api/clusters', result);
    };

    this.JoinCluster = function (clusterId, useremail, captchaResponse, result, failure) {
        $.ajax({
            url: self.serviceUrl + '/api/clusters/join/' + clusterId,
            type: 'POST',
            contentType: 'application/json',
            datatype: 'json',
            data: "{ UserEmail: '" + useremail + "', CaptchaResponse: '" + captchaResponse + "' }"
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

function Dialog() {

    var overlay = $('.overlay');

    this.Show = function ($modal) {
        overlay.fadeIn(300);
        $modal.fadeIn(300);
    };

    this.Hide = function ($modal) {
        overlay.fadeOut(200);
        $modal.fadeOut(200);
    };

    this.Change = function ($from, $to) {
        $from.fadeOut(200);
        $to.fadeIn(300);
    };

    overlay.click(function () {
        $(this).fadeOut(200);
        $('.dialog').fadeOut(200);
    });
};

function SelectedCluster(id, name, autoRetry) {
    var self = this;
    this.Id = id;
    this.Name = name;
    this.AutoRetry = autoRetry;
}

function PartyClusters(api) {
    var self = this;
    this.api = api;
    this.refreshRate = 5000;
    this.joinClusterDialog = new Dialog();
    this.selectedCluster;

    this.Initialize = function () {
        this.PopulateClusterList();
        $('.party-now-button').click(function () {
            try {
                var id = self.SelectRandomCluster();
                self.selectedCluster = new SelectedCluster(id, 'Party now!', true);
                self.ShowJoinClusterDialog();
            }
            catch (exception) {
                alert('There are currently no clusters available. Please try again later.');
            }
        });

        $('.join-now-button').click(function () {
            self.JoinCluster();
        });

        setInterval(this.PopulateClusterList, self.refreshRate);
    };

    this.JoinCluster = function () {
        var joinClusterWindow = $('.join-cluster-dialog');
        var email = $('#join-useremail').val();
        var captchaResponse = $("#g-recaptcha-response").val();

        self.api.JoinCluster(
            self.selectedCluster.Id,
            email,
			captchaResponse,
            function (data) {
                var joinClusterSuccessWindow = $('.join-cluster-dialog-success');
                self.joinClusterDialog.Change(joinClusterWindow, joinClusterSuccessWindow);
            },
            function (data) {
                if (self.selectedCluster.AutoRetry) {
                    switch (data.Code) {
                        case "ClusterFull":
                        case "ClusterExpired":
                        case "ClusterNotReady":
                        case "ClusterDoesNotExist":
                        case "NoPortsAvailable":
                            var id = self.SelectRandomCluster();
                            self.selectedCluster = new SelectedCluster(id, 'Party now!', true);
                            setTimeout(self.JoinCluster, self.refreshRate)
                            return;
                    }
                }

                var failedClusterWindow = $('.join-cluster-dialog-failed');
                $('p', failedClusterWindow).text(data.Message);

                self.joinClusterDialog.Change(joinClusterWindow, failedClusterWindow);
            }
        );
    }

    this.ShowJoinClusterDialog = function () {
        var joinClusterWindow = $('.join-cluster-dialog');
        self.joinClusterDialog.Show(joinClusterWindow);

        grecaptcha.reset();
        $('h3', joinClusterWindow).text(self.selectedCluster.Name);
        $('#join-useremail', joinClusterWindow).val('');
    };


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
                                    .click(function () {
                                        self.selectedCluster = new SelectedCluster(jObject.ClusterId, jObject.Name, false);
                                        self.ShowJoinClusterDialog();
                                    })))
            .appendTo(clusterTable);

            });
        });
    };
};
