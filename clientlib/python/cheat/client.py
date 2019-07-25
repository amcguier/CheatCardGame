import requests
from urllib import parse

class Client():
    _api_url = ""
    _username = ""
    _game_running = False
    _game_id = None
    _players = 0
    _players_connected = 0
    _player_id = None

    _position = None
    _dealer = False
    _hand = None


    
    @property
    def game_id(self):
        return self._game_id

    @game_id.setter
    def game_id(self,value):
        self._game_id = value

    @property
    def players_connected(self):
        return self._players_connected

    @property
    def username(self):
        return self._username
    
    @property
    def players(self):
        return self._players
    
    @property
    def api_url(self):
        return self._api_url

    @api_url.setter
    def api_url(self,value):
        if not self._game_running:
            self._api_url = value


    @property
    def position(self):
        return self._position

    @property
    def dealer(self):
        return self._dealer

    @property
    def hand(self):
        return self._hand
    
    def __init__(self,username,game_id=None):
        self._api_url = "https://cheatcardgame.com/api/"
        self._username = username
        self._game_id = game_id


    def _game_url(self):
        game_parts = "games/{}".format(self.game_id)
        return parse.urljoin(self.api_url,game_parts)

    def _update_game_state(self,game_dict):
        self._game_id = game_dict['GameId']
        self._players = game_dict['Players']
        self._players_connected = game_dict['PlayersConnected']


    def _update_player_hand(self,hand_list):
        self._hand = []

        for entry in hand_list:
            card = {'Suit' : entry['Suit']}
            value = entry['Value']
            
            if isinstance(value,str):
                if value == 'Jack':
                    card['Value'] = 11
                elif value == 'Queen':
                    card['Value'] = 12
                elif value == 'King':
                    card['Value'] = 13
                elif value == 'Ace':
                    card['Value'] = 1
                else:
                    raise Exception("Invalid card returned {}".format(entry))
            else:
                card['Value'] = int(value[1])
            
            self._hand.append(card)                        
        
    def _update_player(self,player_dict):
        self._player_id = player_dict['Id']
        self._position = int(player_dict['Position'])
        self._dealer = player_dict['Dealer']

        if 'Hand' in player_dict:
            self._update_player_hand(player_dict['Hand'])



    def _cards_to_server(self,cards):
        card_list = []
        for card in cards:
            server_card = { 'Suit' : card['Suit']}
            value = card['Value']
            new_value = None
            if value == 1:
                new_value = 'Ace'
            elif value == 11:
                new_value =  'Jack'
            elif value == 12:
                new_value = 'Queen'
            elif value == 13:
                new_value = 'King'
            else:
                new_value = ["Number",value]

            server_card['Value'] = new_value
            card_list.append(server_card)
        return card_list            

            
    def _log_error_response(self,response):
        print("Error:",response.status_code,response.text)
        
    def update_game(self):
        if self.game_id is None:
            return False

        url = self._game_url()
        print("update url",url)
        response = requests.get(url)

        if not response:
            self._log_error_response(response)
            return False

        response_dict = response.json()

        self._update_game_state(response_dict)
        return True                    
        
    def create_game(self,players = 2):
        url = parse.urljoin(self.api_url,"games")
        print("start_game url: ",url)
        new_game = {}
        new_game['Username'] = self._username
        new_game['Players'] = players
        response = requests.post(url,json=new_game)

        if response:
            response_dict = response.json()
            self._update_game_state(response_dict)

            return True
        else:
            self._log_error_response(response)        
            return False

    def list_games(self):
        url = parse.urljoin(self.api_url,"games")
        response = requests.get(url)

        if response:
            return response.json()
        else:
            self._log_error_response(response)            
            return None


    def start_game(self):
        fragment = "games/{}/start".format(self.game_id)
        url = parse.urljoin(self.api_url,fragment)
        response = requests.post(url)

        if response:
            self._update_game_state(response.json())
            self.update_player_info()
            return True
        else:
            self._log_error_response(response)                            
        
    def join_game(self):
        if self.game_id is None or self.username == '':
            print("invalid client info, no game_id or username set")
            return False

        url = parse.urljoin(self.api_url,"games/{}/players".format(self.game_id))

        new_player = { "Username" : self.username }
        response = requests.post(url,json=new_player)

        if response:
            response_dict = response.json()
            print(response_dict)
            self._update_player(response_dict)
            return True
        else:
            self._log_error_response(response)
            return False

    def update_player_info(self):
        if self._player_id is None or self.game_id is None:
            print('You must join a game to update the player info')
            return False

        fragment = 'games/{}/players/{}'.format(self.game_id,self._player_id)
        url = parse.urljoin(self.api_url,fragment)

        response = requests.get(url)

        if response:
            self._update_player(response.json())
            return True
        else:
            self._log_error_response(response)
            return False
                        
    def get_current_turn(self):        
        if self._player_id is None or self.game_id is None:
            print('You need to join a game to get the turn')
            return None
        
        fragment = "games/{}/players/{}/turns".format(self.game_id,self._player_id)
        url = parse.urljoin(self.api_url,fragment)

        response = requests.get(url)

        if not response:
            self._log_error_response(response)
            return None
        else:
            return response.json()

    def play_pass(self):
        if self._player_id is None or self.game_id is None:
            print('You need to join a game to make a play')
            return None
        fragment = "games/{}/players/{}/turns/pass".format(self.game_id,self._player_id)
        url = parse.urljoin(self.api_url,fragment)
        response = requests.post(url)
        
        if not response:
            self._log_error_response(response)
            return None
        else:
            return response.json()        
        pass

    def play_call(self):
        if self._player_id is None or self.game_id is None:
            print('You need to join a game to make a play')
            return None
        fragment = "games/{}/players/{}/turns/call".format(self.game_id,self._player_id)
        url = parse.urljoin(self.api_url,fragment)
        
        response = requests.post(url)        
        if not response:
            self._log_error_response(response)
            return None
        else:
            return response.json()        
        pass
                                                  
    def play_cards(self,cards):
        if self._player_id is None or self.game_id is None:
            print('You need to join a game to make a play')
            return None
        fragment = "games/{}/players/{}/turns/play".format(self.game_id,self._player_id)

        url = parse.urljoin(self.api_url,fragment)
        to_send = self._cards_to_server(cards)        
        response = requests.post(url,json=to_send)
        
        if not response:
            self._log_error_response(response)
            return None
        else:
            return response.json()        
        pass

        pass
